using RagAgentConsole.Services;
using Xunit;

namespace RagAgentConsole.Tests;

public class TelegramBotClientTests
{
    [Fact]
    public void BuildUri_PreservesTelegramTokenAsPathWhenTokenContainsColon()
    {
        var uri = TelegramBotClient.BuildUri(
            "https://api.telegram.org",
            "/bot123456:secret-token/getMe");

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("api.telegram.org", uri.Host);
        Assert.Equal("/bot123456:secret-token/getMe", uri.AbsolutePath);
    }
}
