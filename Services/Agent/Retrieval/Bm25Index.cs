using RagAgentConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace RagAgentConsole.Services;

public interface IBm25Index
{
    /// <summary>True once the index has been built at least once.</summary>
    bool IsBuilt { get; }

    /// <summary>Number of documents (chunks) currently indexed.</summary>
    int DocumentCount { get; }

    /// <summary>Average document length (in tokens) across the corpus.</summary>
    double AverageDocumentLength { get; }

    /// <summary>
    /// Score a document against a query using BM25.
    /// Caller has already tokenized both sides.
    /// </summary>
    double Score(IReadOnlyList<string> queryTokens, IReadOnlyList<string> documentTokens);

    /// <summary>
    /// Rebuild corpus statistics by reading all chunks from the database.
    /// Safe to call concurrently with Score(); readers will see the previous
    /// snapshot until the rebuild completes.
    /// </summary>
    Task RebuildAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuild corpus statistics from an in-memory collection of documents.
    /// Useful for evaluation harnesses and unit tests that need
    /// deterministic corpus statistics without going through the database.
    /// </summary>
    void RebuildFromCorpus(IEnumerable<string> documents);
}

/// <summary>
/// In-memory BM25 index that holds corpus-level statistics
/// (document count, average length, document frequency per term).
/// The index is rebuilt at startup and after each sync.
/// </summary>
/// <remarks>
/// BM25 formula:
///   score(D, Q) = Σ idf(t) * (tf(t,D) * (k1+1)) / (tf(t,D) + k1 * (1 - b + b * |D|/avgdl))
///   idf(t)     = ln((N - df(t) + 0.5) / (df(t) + 0.5) + 1)
/// where:
///   N      = total document count
///   df(t)  = number of documents containing term t
///   tf     = term frequency in document D
///   |D|    = document length in tokens
///   avgdl  = average document length
///   k1     = TF saturation (typically 1.2 – 2.0)
///   b      = length normalization (typically 0.75)
/// </remarks>
public sealed class InMemoryBm25Index(
    IServiceScopeFactory scopeFactory,
    ITokenizer tokenizer,
    ILogger<InMemoryBm25Index> logger) : IBm25Index
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private volatile Snapshot _snapshot = Snapshot.Empty;

    public bool IsBuilt => _snapshot.DocumentCount > 0;
    public int DocumentCount => _snapshot.DocumentCount;
    public double AverageDocumentLength => _snapshot.AverageDocumentLength;

    public double Score(IReadOnlyList<string> queryTokens, IReadOnlyList<string> documentTokens)
    {
        var snapshot = _snapshot;
        if (snapshot.DocumentCount == 0 || queryTokens.Count == 0 || documentTokens.Count == 0)
        {
            return 0;
        }

        var documentLength = documentTokens.Count;
        var termFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in documentTokens)
        {
            termFrequencies[token] = termFrequencies.TryGetValue(token, out var count) ? count + 1 : 1;
        }

        var score = 0d;
        var lengthNormalization = 1 - B + B * documentLength / snapshot.AverageDocumentLength;

        // Deduplicate query terms; a term repeated in the query should not double-count.
        foreach (var term in queryTokens.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!termFrequencies.TryGetValue(term, out var tf) || tf == 0)
            {
                continue;
            }

            var idf = snapshot.GetIdf(term);
            if (idf <= 0)
            {
                continue;
            }

            var numerator = tf * (K1 + 1);
            var denominator = tf + K1 * lengthNormalization;
            score += idf * (numerator / denominator);
        }

        return score;
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var advisoryChunks = await dbContext.SecurityAdvisoryChunks
            .AsNoTracking()
            .Select(chunk => chunk.ChunkText)
            .ToListAsync(cancellationToken);

        var knowledgeChunks = await dbContext.KnowledgeDocumentChunks
            .AsNoTracking()
            .Select(chunk => chunk.ChunkText)
            .ToListAsync(cancellationToken);

        RebuildFromCorpus(advisoryChunks.Concat(knowledgeChunks));
    }

    public void RebuildFromCorpus(IEnumerable<string> documents)
    {
        var documentLengths = new List<int>();
        var documentFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            var tokens = tokenizer.Tokenize(document);
            if (tokens.Count == 0)
            {
                continue;
            }

            documentLengths.Add(tokens.Count);
            foreach (var distinctTerm in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                documentFrequency[distinctTerm] = documentFrequency.TryGetValue(distinctTerm, out var count) ? count + 1 : 1;
            }
        }

        var documentCount = documentLengths.Count;
        var averageLength = documentCount == 0 ? 1.0 : documentLengths.Average();
        var snapshot = new Snapshot(documentCount, averageLength, documentFrequency);

        Interlocked.Exchange(ref _snapshot, snapshot);
        logger.LogInformation(
            "BM25 index rebuilt. DocumentCount={DocumentCount}, AverageLength={AverageLength:0.0}, UniqueTerms={UniqueTerms}.",
            documentCount,
            averageLength,
            documentFrequency.Count);
    }

    private sealed class Snapshot
    {
        public static readonly Snapshot Empty = new(0, 1.0, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        private readonly IReadOnlyDictionary<string, int> _documentFrequency;

        public Snapshot(int documentCount, double averageDocumentLength, IReadOnlyDictionary<string, int> documentFrequency)
        {
            DocumentCount = documentCount;
            AverageDocumentLength = averageDocumentLength <= 0 ? 1.0 : averageDocumentLength;
            _documentFrequency = documentFrequency;
        }

        public int DocumentCount { get; }
        public double AverageDocumentLength { get; }

        public double GetIdf(string term)
        {
            if (!_documentFrequency.TryGetValue(term, out var df))
            {
                // Unknown term: emit a small positive IDF so query-only signals
                // (e.g. CVE IDs never seen at index time) still contribute weakly.
                return Math.Log(1 + (DocumentCount + 0.5) / 1.5);
            }

            return Math.Log(1 + (DocumentCount - df + 0.5) / (df + 0.5));
        }
    }
}

/// <summary>
/// Hosted service that builds the BM25 index once at application startup.
/// Failures are logged but do not crash the host; the index will simply
/// return 0 for all scores until the next refresh succeeds.
/// </summary>
public sealed class Bm25IndexInitializationService(
    IBm25Index index,
    ILogger<Bm25IndexInitializationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await index.RebuildAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Initial BM25 index build failed; retrieval will fall back to zero text scores until a successful refresh.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
