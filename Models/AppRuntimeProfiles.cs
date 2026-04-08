namespace CPBLLineBotCloud.Models;

/// <summary>
/// 集中整理常用的 runtime profile，讓部署時優先選 profile，
/// 只有真的需要微調時才再覆寫細部 role 開關。
/// </summary>
public static class AppRuntimeProfiles
{
    public const string Standard = "Standard";
    public const string WorkerOnly = "WorkerOnly";
    public const string IngressOnly = "IngressOnly";
    public const string PollingNode = "PollingNode";
    public const string Custom = "Custom";

    public static string Normalize(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return Standard;
        }

        return profile.Trim() switch
        {
            var value when value.Equals(Standard, StringComparison.OrdinalIgnoreCase) => Standard,
            var value when value.Equals(WorkerOnly, StringComparison.OrdinalIgnoreCase) => WorkerOnly,
            var value when value.Equals(IngressOnly, StringComparison.OrdinalIgnoreCase) => IngressOnly,
            var value when value.Equals(PollingNode, StringComparison.OrdinalIgnoreCase) => PollingNode,
            var value when value.Equals(Custom, StringComparison.OrdinalIgnoreCase) => Custom,
            _ => Custom
        };
    }
}
