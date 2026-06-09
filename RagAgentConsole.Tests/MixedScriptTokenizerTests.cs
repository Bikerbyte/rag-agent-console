using RagAgentConsole.Services;
using Xunit;

namespace RagAgentConsole.Tests;

public class MixedScriptTokenizerTests
{
    private static readonly MixedScriptTokenizer Tokenizer = new();

    [Fact]
    public void Tokenize_LowercasesLatinTokens()
    {
        var tokens = Tokenizer.Tokenize("Cisco IOS Vulnerability");

        Assert.Equal(["cisco", "ios", "vulnerability"], tokens);
    }

    [Fact]
    public void Tokenize_SplitsOnPunctuationAndWhitespace()
    {
        var tokens = Tokenizer.Tokenize("citrix,netscaler;buffer-overflow");

        Assert.Equal(["citrix", "netscaler", "buffer", "overflow"], tokens);
    }

    [Fact]
    public void Tokenize_EmitsEachCjkCharacterAsItsOwnToken()
    {
        var tokens = Tokenizer.Tokenize("資訊安全弱點");

        // CJK has no word boundaries, so per-character tokenization is the
        // standard cheap baseline. "的", "了", and similar functional chars
        // would be filtered, but these content chars all stay.
        Assert.Equal(["資", "訊", "安", "全", "弱", "點"], tokens);
    }

    [Fact]
    public void Tokenize_HandlesMixedLatinAndCjk()
    {
        var tokens = Tokenizer.Tokenize("Citrix NetScaler 弱點");

        Assert.Contains("citrix", tokens);
        Assert.Contains("netscaler", tokens);
        Assert.Contains("弱", tokens);
        Assert.Contains("點", tokens);
    }

    [Fact]
    public void Tokenize_RemovesStopWords()
    {
        var tokens = Tokenizer.Tokenize("the firewall and the gateway");

        Assert.DoesNotContain("the", tokens);
        Assert.DoesNotContain("and", tokens);
        Assert.Contains("firewall", tokens);
        Assert.Contains("gateway", tokens);
    }

    [Fact]
    public void Tokenize_RemovesChineseFunctionalCharacters()
    {
        var tokens = Tokenizer.Tokenize("這個的弱點");

        Assert.DoesNotContain("的", tokens);
        Assert.Contains("弱", tokens);
        Assert.Contains("點", tokens);
    }

    [Fact]
    public void Tokenize_KeepsVersionLikeDigits()
    {
        var tokens = Tokenizer.Tokenize("netscaler 59.22 patch");

        // 59 and 22 stay (both >= 2 chars), version separator drops them.
        Assert.Contains("59", tokens);
        Assert.Contains("22", tokens);
        Assert.Contains("netscaler", tokens);
        Assert.Contains("patch", tokens);
    }

    [Fact]
    public void Tokenize_DropsSingleCharacterLatin()
    {
        // Single Latin chars are too noisy (a, i, x).
        // Single CJK chars are kept because they carry meaning.
        var tokens = Tokenizer.Tokenize("a firewall");

        Assert.DoesNotContain("a", tokens);
        Assert.Contains("firewall", tokens);
    }

    [Fact]
    public void Tokenize_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(Tokenizer.Tokenize(""));
        Assert.Empty(Tokenizer.Tokenize("   "));
        Assert.Empty(Tokenizer.Tokenize(null!));
    }
}
