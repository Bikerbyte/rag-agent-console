namespace SecurityAdvisoryBot.Models;

public class SecurityAdvisoryOptions
{
    public const string SectionName = "SecurityAdvisories";

    public bool EnableCisaKevSource { get; set; } = true;
    public bool EnableNvdSource { get; set; } = true;
    public string CisaKevJsonUrl { get; set; } = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";
    public string NvdApiBaseUrl { get; set; } = "https://services.nvd.nist.gov";
    public int NvdLookbackDays { get; set; } = 7;
    public int MaxNvdResultsPerSync { get; set; } = 100;
    public int EmbeddingDimensions { get; set; } = 384;
    public int RagMaxChunks { get; set; } = 5;
}
