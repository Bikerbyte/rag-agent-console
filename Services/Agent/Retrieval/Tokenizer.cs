using System.Globalization;
using System.Text;

namespace SecurityAdvisoryBot.Services;

public interface ITokenizer
{
    IReadOnlyList<string> Tokenize(string text);
}

/// <summary>
/// Tokenizer that handles mixed Latin + CJK text.
/// Latin runs are lowercased and split on whitespace / punctuation.
/// CJK characters are emitted as single-character tokens
/// (since CJK has no whitespace word boundaries).
/// Stop words and very short tokens are filtered.
/// </summary>
public sealed class MixedScriptTokenizer : ITokenizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        "a", "an", "the", "and", "or", "of", "in", "on", "at", "to", "for", "with",
        "is", "are", "was", "were", "be", "been", "being", "has", "have", "had",
        "do", "does", "did", "will", "would", "should", "could", "can", "may", "might",
        "this", "that", "these", "those", "it", "its", "as", "by", "from",
        // Chinese functional
        "的", "了", "是", "在", "和", "與", "或", "也", "嗎", "呢", "吧", "有", "沒有"
    };

    public IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = new List<string>();
        var latinBuffer = new StringBuilder();

        foreach (var character in text)
        {
            if (IsCjk(character))
            {
                FlushLatin(latinBuffer, tokens);
                var cjkToken = character.ToString();
                if (!StopWords.Contains(cjkToken))
                {
                    tokens.Add(cjkToken);
                }
            }
            else if (char.IsLetterOrDigit(character))
            {
                latinBuffer.Append(char.ToLowerInvariant(character));
            }
            else
            {
                FlushLatin(latinBuffer, tokens);
            }
        }

        FlushLatin(latinBuffer, tokens);
        return tokens;
    }

    private static void FlushLatin(StringBuilder buffer, List<string> tokens)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        var token = buffer.ToString();
        buffer.Clear();

        // Keep alphanumeric identifiers like cve-2024-1234 (split on '-' above)
        // and version numbers. Drop ultra-short tokens but keep digits-only of length >= 2
        // since they often encode meaningful version numbers (e.g. "2402").
        if (token.Length < 2 || StopWords.Contains(token))
        {
            return;
        }

        tokens.Add(token);
    }

    private static bool IsCjk(char character)
    {
        // CJK Unified Ideographs and common extensions, plus Hiragana / Katakana.
        var category = CharUnicodeInfo.GetUnicodeCategory(character);
        if (category != UnicodeCategory.OtherLetter)
        {
            return false;
        }

        return (character >= 0x4E00 && character <= 0x9FFF)   // CJK Unified
            || (character >= 0x3400 && character <= 0x4DBF)   // CJK Extension A
            || (character >= 0x3040 && character <= 0x309F)   // Hiragana
            || (character >= 0x30A0 && character <= 0x30FF);  // Katakana
    }
}
