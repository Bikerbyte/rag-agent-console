using System.Text.Json;
using System.Text.Json.Serialization;
using RagAgentConsole.Data;
using RagAgentConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace RagAgentConsole.Services;

public interface IRetrievalEvaluationService
{
    /// <summary>Load cases (mapped to the evaluation DTO) from the database.</summary>
    Task<IReadOnlyList<RetrievalEvaluationCase>> LoadCasesAsync(CancellationToken cancellationToken = default);

    /// <summary>List the persisted, editable cases for the management UI.</summary>
    Task<IReadOnlyList<RetrievalEvaluationCaseEntity>> GetManagedCasesAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetch a single persisted case by its primary key.</summary>
    Task<RetrievalEvaluationCaseEntity?> GetCaseAsync(int id, CancellationToken cancellationToken = default);

    Task CreateCaseAsync(RetrievalEvaluationCaseDraft draft, CancellationToken cancellationToken = default);

    Task UpdateCaseAsync(int id, RetrievalEvaluationCaseDraft draft, CancellationToken cancellationToken = default);

    Task DeleteCaseAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Seed the bundled golden set into the database when no cases exist yet.</summary>
    Task SeedCasesIfEmptyAsync(CancellationToken cancellationToken = default);

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
    IRagRetrievalService searchService,
    ApplicationDbContext dbContext,
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
        var entities = await dbContext.RetrievalEvaluationCases
            .AsNoTracking()
            .OrderBy(item => item.RetrievalEvaluationCaseId)
            .ToListAsync(cancellationToken);

        return entities.Select(entity => new RetrievalEvaluationCase(
            entity.CaseKey,
            entity.Question,
            ParseList(entity.ExpectedDocumentTitles, splitOnCommas: false),
            entity.Notes,
            ParseMetadata(entity.ExpectedMetadata),
            ParseList(entity.ExpectedContentKeywords, splitOnCommas: false))).ToList();
    }

    public async Task<IReadOnlyList<RetrievalEvaluationCaseEntity>> GetManagedCasesAsync(CancellationToken cancellationToken = default)
        => await dbContext.RetrievalEvaluationCases
            .AsNoTracking()
            .OrderBy(item => item.RetrievalEvaluationCaseId)
            .ToListAsync(cancellationToken);

    public async Task<RetrievalEvaluationCaseEntity?> GetCaseAsync(int id, CancellationToken cancellationToken = default)
        => await dbContext.RetrievalEvaluationCases
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.RetrievalEvaluationCaseId == id, cancellationToken);

    public async Task CreateCaseAsync(RetrievalEvaluationCaseDraft draft, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var caseKey = await BuildUniqueCaseKeyAsync(draft.Question, cancellationToken);
        dbContext.RetrievalEvaluationCases.Add(new RetrievalEvaluationCaseEntity
        {
            CaseKey = caseKey,
            Question = Trim(draft.Question, 500),
            ExpectedDocumentTitles = NormalizeStored(draft.ExpectedDocumentTitlesText, 2000),
            ExpectedContentKeywords = NormalizeStored(draft.ExpectedContentKeywordsText, 2000),
            ExpectedMetadata = NormalizeStored(draft.ExpectedMetadataText, 2000),
            Notes = NormalizeStored(draft.Notes, 1000),
            IsSeeded = false,
            CreatedTime = now,
            LastUpdatedTime = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCaseAsync(int id, RetrievalEvaluationCaseDraft draft, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.RetrievalEvaluationCases
            .FirstOrDefaultAsync(item => item.RetrievalEvaluationCaseId == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Question = Trim(draft.Question, 500);
        entity.ExpectedDocumentTitles = NormalizeStored(draft.ExpectedDocumentTitlesText, 2000);
        entity.ExpectedContentKeywords = NormalizeStored(draft.ExpectedContentKeywordsText, 2000);
        entity.ExpectedMetadata = NormalizeStored(draft.ExpectedMetadataText, 2000);
        entity.Notes = NormalizeStored(draft.Notes, 1000);
        entity.LastUpdatedTime = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCaseAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.RetrievalEvaluationCases
            .FirstOrDefaultAsync(item => item.RetrievalEvaluationCaseId == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        dbContext.RetrievalEvaluationCases.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedCasesIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.RetrievalEvaluationCases.AnyAsync(cancellationToken))
        {
            return;
        }

        var seedCases = await LoadSeedCasesFromFileAsync(cancellationToken);
        if (seedCases.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var seed in seedCases)
        {
            dbContext.RetrievalEvaluationCases.Add(new RetrievalEvaluationCaseEntity
            {
                CaseKey = Trim(string.IsNullOrWhiteSpace(seed.Id) ? Slugify(seed.Question) : seed.Id, 96),
                Question = Trim(seed.Question, 500),
                ExpectedDocumentTitles = NormalizeStored(string.Join('\n', seed.ExpectedDocumentTitles), 2000),
                ExpectedContentKeywords = NormalizeStored(string.Join('\n', seed.ExpectedContentKeywords ?? []), 2000),
                ExpectedMetadata = NormalizeStored(FormatMetadata(seed.ExpectedMetadata), 2000),
                Notes = NormalizeStored(seed.Notes, 1000),
                IsSeeded = true,
                CreatedTime = now,
                LastUpdatedTime = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} retrieval evaluation cases from the bundled golden set.", seedCases.Count);
    }

    private async Task<IReadOnlyList<RetrievalEvaluationCase>> LoadSeedCasesFromFileAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(hostEnvironment.ContentRootPath, GoldenSetRelativePath);
        if (!File.Exists(path))
        {
            logger.LogWarning("Golden set seed file not found at {Path}.", path);
            return [];
        }

        await using var stream = File.OpenRead(path);
        var payload = await JsonSerializer.DeserializeAsync<GoldenSetPayload>(stream, JsonOptions, cancellationToken);
        return payload?.Cases is null
            ? []
            : payload.Cases.Select(item => new RetrievalEvaluationCase(
                item.Id,
                item.Question,
                item.ExpectedDocumentTitles ?? [],
                item.Notes,
                item.ExpectedMetadata,
                item.ExpectedContentKeywords ?? [])).ToList();
    }

    private async Task<string> BuildUniqueCaseKeyAsync(string question, CancellationToken cancellationToken)
    {
        var baseKey = Slugify(question);
        var candidate = baseKey;
        var suffix = 2;
        while (await dbContext.RetrievalEvaluationCases.AnyAsync(item => item.CaseKey == candidate, cancellationToken))
        {
            candidate = Trim($"{baseKey}-{suffix}", 96);
            suffix++;
        }

        return candidate;
    }

    private static string Slugify(string value)
    {
        var slug = new string((value ?? string.Empty)
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        slug = Trim(slug, 48).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"case-{Guid.NewGuid():N}"[..12] : slug;
    }

    private static string? NormalizeStored(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lines = value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Trim(string.Join('\n', lines), maxLength);
    }

    private static IReadOnlyDictionary<string, string?>? ParseMetadata(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var pairs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in stored.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
            {
                pairs[key] = value;
            }
        }

        return pairs.Count == 0 ? null : pairs;
    }

    private static string? FormatMetadata(IReadOnlyDictionary<string, string?>? metadata)
        => metadata is null or { Count: 0 }
            ? null
            : string.Join('\n', metadata
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{pair.Key.Trim()}={pair.Value!.Trim()}"));

    private static IReadOnlyList<string> ParseList(string? stored, bool splitOnCommas)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        var separators = splitOnCommas ? new[] { '\n', '\r', ',' } : ['\n', '\r'];
        return stored
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string Trim(string value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
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

        var expectedTitles = new HashSet<string>(evalCase.ExpectedDocumentTitles, StringComparer.OrdinalIgnoreCase);
        var expectedContentKeywords = evalCase.ExpectedContentKeywords ?? [];
        var expectedMetadata = evalCase.ExpectedMetadata;

        var matches = new List<RetrievalEvaluationMatch>();
        int? firstRelevantRank = null;
        for (var index = 0; index < searchResponse.Results.Count; index++)
        {
            var result = searchResponse.Results[index];
            var rank = index + 1;
            var isRelevant = IsRelevant(result, expectedTitles, expectedContentKeywords, expectedMetadata);
            if (isRelevant && firstRelevantRank is null)
            {
                firstRelevantRank = rank;
            }

            matches.Add(new RetrievalEvaluationMatch(
                rank,
                result.Title,
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

    private bool IsRelevant(
        RetrievalResult result,
        HashSet<string> expectedTitles,
        IReadOnlyList<string> expectedContentKeywords,
        IReadOnlyDictionary<string, string?>? expectedMetadata)
    {
        if (expectedTitles.Count > 0 && expectedTitles.Contains(result.Title))
        {
            return true;
        }

        if (expectedContentKeywords.Any(keyword =>
                result.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                result.ChunkText.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (expectedMetadata is { Count: > 0 })
        {
            var metadata = result.BuildMetadata();
            return expectedMetadata.All(pair =>
                metadata.TryGetValue(pair.Key, out var value) &&
                string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));
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

        [JsonPropertyName("expectedDocumentTitles")]
        public List<string>? ExpectedDocumentTitles { get; set; }

        [JsonPropertyName("expectedContentKeywords")]
        public List<string>? ExpectedContentKeywords { get; set; }

        [JsonPropertyName("expectedMetadata")]
        public Dictionary<string, string?>? ExpectedMetadata { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }
}
