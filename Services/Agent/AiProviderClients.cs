using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RagAgentConsole.Models;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Services;

public interface IAiChatClient
{
    Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}

public interface IRagEmbeddingService
{
    Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public class AiChatClient(
    HttpClient httpClient,
    IAppSettingsService appSettingsService,
    ILogger<AiChatClient> logger) : IAiChatClient
{
    public async Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var currentOptions = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);
        if (!currentOptions.EnableChatGeneration)
        {
            return null;
        }

        try
        {
            if (IsOpenAi(currentOptions.Provider))
            {
                return await CompleteWithOpenAiAsync(currentOptions, systemPrompt, userPrompt, cancellationToken);
            }

            if (IsOllama(currentOptions.Provider))
            {
                return await CompleteWithOllamaAsync(currentOptions, systemPrompt, userPrompt, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "AI chat provider {Provider} failed.", currentOptions.Provider);
            if (!currentOptions.UseLocalFallback)
            {
                throw;
            }
        }

        return null;
    }

    private async Task<string?> CompleteWithOpenAiAsync(
        AiProviderOptions currentOptions,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentOptions.OpenAiApiKey))
        {
            logger.LogInformation("OpenAI chat provider selected but no API key is configured.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(currentOptions.OpenAiApiBaseUrl, "/v1/chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentOptions.OpenAiApiKey);
        request.Content = JsonContent.Create(new
        {
            model = currentOptions.OpenAiChatModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private async Task<string?> CompleteWithOllamaAsync(
        AiProviderOptions currentOptions,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(currentOptions.OllamaApiBaseUrl, "/api/chat"));
        request.Content = JsonContent.Create(new
        {
            model = currentOptions.OllamaChatModel,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private static Uri BuildUri(string baseUrl, string path)
        => new(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    private static bool IsOpenAi(string provider)
        => string.Equals(provider, AiProviderNames.OpenAI, StringComparison.OrdinalIgnoreCase);

    private static bool IsOllama(string provider)
        => string.Equals(provider, AiProviderNames.Ollama, StringComparison.OrdinalIgnoreCase);
}

public partial class RagEmbeddingService(
    HttpClient httpClient,
    IOptions<SecurityAdvisoryOptions> advisoryOptions,
    IAppSettingsService appSettingsService,
    ITokenizer tokenizer,
    ILogger<RagEmbeddingService> logger) : IRagEmbeddingService
{
    public async Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var currentOptions = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);

        try
        {
            if (string.Equals(currentOptions.Provider, AiProviderNames.OpenAI, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(currentOptions.OpenAiApiKey))
            {
                return await BuildOpenAiEmbeddingAsync(currentOptions, text, cancellationToken);
            }

            if (string.Equals(currentOptions.Provider, AiProviderNames.Ollama, StringComparison.OrdinalIgnoreCase))
            {
                return await BuildOllamaEmbeddingAsync(currentOptions, text, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "AI embedding provider {Provider} failed.", currentOptions.Provider);
            if (!currentOptions.UseLocalFallback)
            {
                throw;
            }
        }

        return BuildLocalHashEmbedding(text);
    }

    private async Task<float[]> BuildOpenAiEmbeddingAsync(
        AiProviderOptions currentOptions,
        string text,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(currentOptions.OpenAiApiBaseUrl, "/v1/embeddings"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentOptions.OpenAiApiKey);
        request.Content = JsonContent.Create(new
        {
            model = currentOptions.OpenAiEmbeddingModel,
            input = text
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadFloatArray(document.RootElement.GetProperty("data")[0].GetProperty("embedding"));
    }

    private async Task<float[]> BuildOllamaEmbeddingAsync(
        AiProviderOptions currentOptions,
        string text,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(currentOptions.OllamaApiBaseUrl, "/api/embeddings"));
        request.Content = JsonContent.Create(new
        {
            model = currentOptions.OllamaEmbeddingModel,
            prompt = text
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadFloatArray(document.RootElement.GetProperty("embedding"));
    }

    private float[] BuildLocalHashEmbedding(string text)
    {
        var dimensions = Math.Clamp(advisoryOptions.Value.EmbeddingDimensions, 64, 2048);
        var vector = new float[dimensions];

        // 與 BM25 共用同一把中英混排斷詞器，CJK 文字才進得了向量空間。
        foreach (var token in tokenizer.Tokenize(text))
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var bucket = BitConverter.ToUInt32(bytes, 0) % dimensions;
            var sign = (bytes[4] & 1) == 0 ? 1f : -1f;
            vector[bucket] += sign;
        }

        Normalize(vector);
        return vector;
    }

    private static float[] ReadFloatArray(JsonElement array)
    {
        var vector = new float[array.GetArrayLength()];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            vector[index++] = item.GetSingle();
        }

        Normalize(vector);
        return vector;
    }

    private static void Normalize(float[] vector)
    {
        var sum = 0d;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        var length = Math.Sqrt(sum);
        if (length <= 0)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / length);
        }
    }

    private static Uri BuildUri(string baseUrl, string path)
        => new(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
}
