using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SecurityAdvisoryBot.Services;
using Xunit;

namespace SecurityAdvisoryBot.Tests;

public class OpenAiCredentialValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WhenModelsAreAvailable_ReturnsValid()
    {
        var validator = CreateValidator(HttpStatusCode.OK, """
            {"data":[{"id":"gpt-4o-mini"},{"id":"text-embedding-3-small"}]}
            """);

        var result = await validator.ValidateAsync(BuildRequest());

        Assert.True(result.IsValid);
        Assert.Equal(OpenAiCredentialValidationStatus.Valid, result.Status);
    }

    [Fact]
    public async Task ValidateAsync_WhenApiKeyIsRejected_ReturnsInvalidApiKey()
    {
        var validator = CreateValidator(HttpStatusCode.Unauthorized, """
            {"error":{"code":"invalid_api_key"}}
            """);

        var result = await validator.ValidateAsync(BuildRequest());

        Assert.False(result.IsValid);
        Assert.Equal(OpenAiCredentialValidationStatus.InvalidApiKey, result.Status);
    }

    [Fact]
    public async Task ValidateAsync_WhenConfiguredModelIsUnavailable_ReturnsModelUnavailable()
    {
        var validator = CreateValidator(HttpStatusCode.OK, """
            {"data":[{"id":"gpt-4o-mini"}]}
            """);

        var result = await validator.ValidateAsync(BuildRequest());

        Assert.False(result.IsValid);
        Assert.Equal(OpenAiCredentialValidationStatus.ModelUnavailable, result.Status);
        Assert.Contains("text-embedding-3-small", result.UnavailableModels);
    }

    [Fact]
    public async Task ValidateAsync_WhenRateLimited_DoesNotClaimKeyIsInvalid()
    {
        var validator = CreateValidator(HttpStatusCode.TooManyRequests, "{}");

        var result = await validator.ValidateAsync(BuildRequest());

        Assert.False(result.IsValid);
        Assert.Equal(OpenAiCredentialValidationStatus.RateLimited, result.Status);
    }

    private static OpenAiCredentialValidator CreateValidator(HttpStatusCode statusCode, string content)
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        });

        return new OpenAiCredentialValidator(
            new HttpClient(handler),
            NullLogger<OpenAiCredentialValidator>.Instance);
    }

    private static OpenAiCredentialValidationRequest BuildRequest()
        => new(
            "https://api.openai.com",
            "sk-test",
            "gpt-4o-mini",
            "text-embedding-3-small");

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
