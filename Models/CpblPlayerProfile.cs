namespace CPBLLineBotCloud.Models;

public class CpblPlayerProfile
{
    public required string AccountId { get; set; }
    public required string Name { get; set; }
    public string? TeamName { get; set; }
    public string? JerseyNumber { get; set; }
    public string? Position { get; set; }
    public string? ThrowsBats { get; set; }
    public string? HeightWeight { get; set; }
    public string? BirthDate { get; set; }
}
