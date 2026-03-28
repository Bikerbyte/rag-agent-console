using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

public class TeamInfo
{
    public int TeamInfoId { get; set; }

    [MaxLength(16)]
    public required string TeamCode { get; set; }

    [MaxLength(64)]
    public required string TeamName { get; set; }

    [MaxLength(64)]
    public required string DisplayName { get; set; }
}
