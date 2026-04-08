using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

public class RuntimeNodeHeartbeat
{
    public int RuntimeNodeHeartbeatId { get; set; }

    [MaxLength(128)]
    public required string InstanceName { get; set; }

    [MaxLength(128)]
    public required string MachineName { get; set; }

    [MaxLength(64)]
    public required string EnvironmentName { get; set; }

    [MaxLength(400)]
    public required string RoleSummary { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "Online";

    public int ProcessId { get; set; }
    public DateTimeOffset ProcessStartedTime { get; set; }
    public DateTimeOffset LastSeenTime { get; set; }

    [MaxLength(64)]
    public string? AppVersion { get; set; }
}
