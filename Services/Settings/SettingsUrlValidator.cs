namespace SecurityAdvisoryBot.Services;

public static class SettingsUrlValidator
{
    public static bool IsAbsoluteHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static string UseFallbackUnlessAbsoluteHttpUrl(string? value, string fallback)
        => IsAbsoluteHttpUrl(value) ? value!.Trim() : fallback;
}
