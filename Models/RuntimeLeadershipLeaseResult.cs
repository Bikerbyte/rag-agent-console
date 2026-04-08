namespace CPBLLineBotCloud.Models;

public class RuntimeLeadershipLeaseResult
{
    public required string LeaseName { get; init; }
    public required string InstanceName { get; init; }
    public required string CurrentOwnerInstanceName { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsLeader { get; init; }
    public required string Action { get; init; }
}
