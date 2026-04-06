using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

/// <summary>
/// 記錄 scheduled job 目前由哪個節點持有執行租約，讓多節點部署時仍維持單活執行。
/// </summary>
public class RuntimeLeadershipLease
{
    [MaxLength(128)]
    public required string LeaseName { get; set; }

    [MaxLength(128)]
    public required string OwnerInstanceName { get; set; }

    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset RenewedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}
