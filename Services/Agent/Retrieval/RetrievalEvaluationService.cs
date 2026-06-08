using System.Text.Json;
using System.Text.Json.Serialization;
using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface IRetrievalEvaluationService
{
    /// <summary>Load cases from the configured golden set on disk.</summary>
    Task<IReadOnlyList<RetrievalEvaluationCase>> LoadCasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Run every case through each requested retrieval strategy and
    /// return per-strategy Hit@1 / Hit@5 / MRR summaries plus per-case details.
    /// </summary>
    Task<RetrievalEvaluationReport> EvaluateAsync(
        IReadOnlyList<string>? retrievalModes = null,
        int topK = 5,
        CancellationToken cancellationToken = default);
}

public sealed class RetrievalEvaluationService(
    ISecurityAdvisorySearchService searchService,
    IWebHostEnvironment hostEnvironment,
    ILogger<RetrievalEvaluationService> logger) : IRetrievalEvaluationService
{
    private const string GoldenSetRelativePath = "Evaluation/golden-set.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] DefaultModes =
    [
        RetrievalModes.Hybrid,
        RetrievalModes.Vector,
        RetrievalModes.Keyword
    ];

    public async Task<IReadOnlyList<RetrievalEvaluationCase>> LoadCasesAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(hostEnvironment.ContentRootPath, GoldenSetRelativePath);
        if (!File.Exists(path))
        {
            logger.LogWarning("Golden set file not found at {Path}.", path);
            return [];
        }

        await using var stream = File.OpenRead(path);
        var payload = await JsonSerializer.DeserializeAsync<GoldenSetPayload>(stream, JsonOptions, cancellationToken);
        return payload?.Cases is null
            ? []
            : payload.Cases.Select(item => new RetrievalEvaluationCase(
                item.Id,
                item.Question,
                item.ExpectedCveIds ?? [],
                item.ExpectedDocumentTitles ?? [],
                item.Notes)).ToList();
    }

    public async Task<RetrievalEvaluationReport> EvaluateAsync(
        IReadOnlyList<string>? retrievalModes = null,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var cases = await LoadCasesAsync(cancellationToken);
        var modes = (retrievalModes is { Count: > 0 } ? retrievalModes : DefaultModes)
            .Select(RetrievalModes.Normalize)
            .Distinct()
            .ToList();

        var summaries = new List<RetrievalEvaluationSummary>();
        foreach (var mode in modes)
        {
            var caseResults = new List<RetrievalEvaluationCaseResult>();
            foreach (var evalCase in cases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var caseResult = await EvaluateCaseAsync(evalCase, mode, topK, cancellationToken);
                caseResults.Add(caseResult);
            }

            summaries.Add(BuildSummary(mode, caseResults));
        }

        return new RetrievalEvaluationReport(DateTimeOffset.UtcNow, summaries);
    }

    private async Task<RetrievalEvaluationCaseResult> EvaluateCaseAsync(
        RetrievalEvaluationCase evalCase,
        string mode,
        int topK,
        CancellationToken cancellationToken)
    {
        var searchResponse = await searchService.SearchWithTraceAsync(
            evalCase.Question,
            history: null,
            maxResults: topK,
            moduleName: null,
            retrievalMode: mode,
            cancellationToken: cancellationToken);

        var expectedCves = new HashSet<string>(evalCase.ExpectedCveIds, StringComparer.OrdinalIgnoreCase);
        var expectedTitles = new HashSet<string>(evalCase.ExpectedDocumentTitles, StringComparer.OrdinalIgnoreCase);

        var matches = new List<RetrievalEvaluationMatch>();
        int? firstRelevantRank = null;
        for (var index = 0; index < searchResponse.Results.Count; index++)
        {
            var result = searchResponse.Results[index];
            var rank = index + 1;
            var isRelevant = IsRelevant(result, expectedCves, expectedTitles);
            if (isRelevant && firstRelevantRank is null)
            {
                firstRelevantRank = rank;
            }

            matches.Add(new RetrievalEvaluationMatch(
                rank,
                result.Title,
                result.CveId,
                result.Score,
                result.VectorScore,
                result.TextScore,
                isRelevant));
        }

        var hitAt1 = firstRelevantRank == 1;
        var hitAt5 = firstRelevantRank is not null && firstRelevantRank <= 5;
        var rr = firstRelevantRank is { } r ? 1.0 / r : 0.0;

        return new RetrievalEvaluationCaseResult(
            evalCase,
            mode,
            hitAt1,
            hitAt5,
            rr,
            firstRelevantRank,
            matches);
    }

    private static bool IsRelevant(
        SecurityAdvisorySearchResult result,
        HashSet<string> expectedCves,
        HashSet<string> expectedTitles)
    {
        if (!string.IsNullOrWhiteSpace(result.CveId) && expectedCves.Contains(result.CveId))
        {
            return true;
        }

        if (expectedTitles.Count > 0 && expectedTitles.Contains(result.Title))
        {
            return true;
        }

        return false;
    }

    private static RetrievalEvaluationSummary BuildSummary(
        string mode,
        IReadOnlyList<RetrievalEvaluationCaseResult> caseResults)
    {
        if (caseResults.Count == 0)
        {
            return new RetrievalEvaluationSummary(mode, 0, 0, 0, 0, []);
        }

        var hitAt1 = caseResults.Count(item => item.HitAt1) / (double)caseResults.Count;
        var hitAt5 = caseResults.Count(item => item.HitAt5) / (double)caseResults.Count;
        var mrr = caseResults.Sum(item => item.ReciprocalRank) / caseResults.Count;

        return new RetrievalEvaluationSummary(mode, caseResults.Count, hitAt1, hitAt5, mrr, caseResults);
    }

    private sealed class GoldenSetPayload
    {
        [JsonPropertyName("cases")]
        public List<GoldenSetCase>? Cases { get; set; }
    }

    private sealed class GoldenSetCase
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [JsonPropertyName("expectedCveIds")]
        public List<string>? ExpectedCveIds { get; set; }

        [JsonPropertyName("expectedDocumentTitles")]
        public List<string>? ExpectedDocumentTitles { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }
}
