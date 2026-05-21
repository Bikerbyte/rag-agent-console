namespace SecurityAdvisoryBot.Services;

public interface IAdvisoryEmbeddingService
{
    Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
