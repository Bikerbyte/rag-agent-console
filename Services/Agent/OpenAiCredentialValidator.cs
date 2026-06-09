using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SecurityAdvisoryBot.Services;

public sealed class OpenAiCredentialValidator(
    HttpClient httpClient,
    ILogger<OpenAiCredentialValidator> logger) : IOpenAiCredentialValidator
{
    public async Task<OpenAiCredentialValidationResult> ValidateAsync(
        OpenAiCredentialValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(request.ApiBaseUrl, "/v1/models"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());

        try
        {
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await ValidateModelsAsync(response, request, cancellationToken);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => Failure(
                    OpenAiCredentialValidationStatus.InvalidApiKey,
                    "OpenAI 拒絕此 API key。請確認 key 正確、尚未撤銷，或重新建立一組 key。"),
                HttpStatusCode.Forbidden => Failure(
                    OpenAiCredentialValidationStatus.Forbidden,
                    "API key 已送達 OpenAI，但目前沒有存取權限；請檢查 project 權限、組織設定或 IP allowlist。"),
                HttpStatusCode.TooManyRequests => Failure(
                    OpenAiCredentialValidationStatus.RateLimited,
                    "OpenAI 暫時拒絕驗證，可能是 rate limit 或帳務額度問題；目前無法確認此 key 可用。"),
                _ when (int)response.StatusCode >= 500 => Failure(
                    OpenAiCredentialValidationStatus.ServiceUnavailable,
                    "OpenAI 驗證服務暫時無法使用，API key 尚未儲存，請稍後再試。"),
                _ => Failure(
                    OpenAiCredentialValidationStatus.Rejected,
                    $"OpenAI 驗證請求失敗（HTTP {(int)response.StatusCode}），API key 尚未儲存。")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                OpenAiCredentialValidationStatus.ServiceUnavailable,
                "OpenAI 驗證逾時，API key 尚未儲存，請稍後再試。");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "OpenAI API key validation request failed.");
            return Failure(
                OpenAiCredentialValidationStatus.ServiceUnavailable,
                "無法連線到 OpenAI 驗證 API，API key 尚未儲存；請檢查 Base URL 與網路連線。");
        }
    }

    private static async Task<OpenAiCredentialValidationResult> ValidateModelsAsync(
        HttpResponseMessage response,
        OpenAiCredentialValidationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return Failure(
                    OpenAiCredentialValidationStatus.InvalidResponse,
                    "OpenAI models API 回傳格式不完整，API key 尚未儲存。");
            }

            var modelIds = data
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out _))
                .Select(item => item.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var unavailableModels = new[] { request.ChatModel, request.EmbeddingModel }
                .Where(model => !string.IsNullOrWhiteSpace(model) && !modelIds.Contains(model.Trim()))
                .Select(model => model.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (unavailableModels.Count > 0)
            {
                return new OpenAiCredentialValidationResult(
                    OpenAiCredentialValidationStatus.ModelUnavailable,
                    $"API key 有效，但目前無法使用設定的模型：{string.Join(", ", unavailableModels)}。",
                    unavailableModels);
            }

            return new OpenAiCredentialValidationResult(
                OpenAiCredentialValidationStatus.Valid,
                "OpenAI API key 與模型存取驗證成功。",
                []);
        }
        catch (JsonException)
        {
            return Failure(
                OpenAiCredentialValidationStatus.InvalidResponse,
                "OpenAI models API 回傳無法辨識的內容，API key 尚未儲存。");
        }
        catch (InvalidOperationException)
        {
            return Failure(
                OpenAiCredentialValidationStatus.InvalidResponse,
                "OpenAI models API 回傳格式不完整，API key 尚未儲存。");
        }
    }

    private static OpenAiCredentialValidationResult Failure(
        OpenAiCredentialValidationStatus status,
        string message)
        => new(status, message, []);

    private static Uri BuildUri(string baseUrl, string path)
        => new(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
}
