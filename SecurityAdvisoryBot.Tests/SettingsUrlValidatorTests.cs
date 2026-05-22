using SecurityAdvisoryBot.Services;
using Xunit;

namespace SecurityAdvisoryBot.Tests;

public class SettingsUrlValidatorTests
{
    [Theory]
    [InlineData("https://api.telegram.org")]
    [InlineData("http://localhost:11434")]
    public void IsAbsoluteHttpUrl_AcceptsHttpAndHttpsUrls(string value)
    {
        Assert.True(SettingsUrlValidator.IsAbsoluteHttpUrl(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bot123456:token")]
    [InlineData("api.telegram.org")]
    [InlineData("ftp://example.com")]
    public void IsAbsoluteHttpUrl_RejectsNonHttpUrls(string value)
    {
        Assert.False(SettingsUrlValidator.IsAbsoluteHttpUrl(value));
    }

    [Fact]
    public void UseFallbackUnlessAbsoluteHttpUrl_ReturnsFallbackForBotTokenLikeValue()
    {
        var value = SettingsUrlValidator.UseFallbackUnlessAbsoluteHttpUrl("bot123456:token", "https://api.telegram.org");

        Assert.Equal("https://api.telegram.org", value);
    }
}
