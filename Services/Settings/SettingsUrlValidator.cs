namespace SecurityAdvisoryBot.Services;

public static class SettingsUrlValidator
{
    public const string DefaultTelegramApiBaseUrl = "https://api.telegram.org";
    public const string DefaultOpenAiApiBaseUrl = "https://api.openai.com";
    public const string DefaultOllamaApiBaseUrl = "http://localhost:11434";

    public static bool IsAbsoluteHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static string UseFallbackUnlessAbsoluteHttpUrl(string? value, string fallback)
        => IsAbsoluteHttpUrl(value) ? value!.Trim() : fallback;
}
