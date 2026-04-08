using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Pages.Runtime;

public class IndexModel(ApplicationDbContext dbContext) : PageModel
{
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromSeconds(45);

    public IReadOnlyList<NodeRowViewModel> Nodes { get; private set; } = [];
    public IReadOnlyList<LeadershipLeaseRowViewModel> LeadershipLeases { get; private set; } = [];
    public IReadOnlyList<TelegramUpdateInbox> RecentInboxItems { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var now = DateTimeOffset.UtcNow;

        var heartbeats = await dbContext.RuntimeNodeHeartbeats
            .OrderByDescending(item => item.LastSeenTime)
            .ToListAsync();

        var leadershipLeases = await dbContext.RuntimeLeadershipLeases
            .OrderBy(item => item.LeaseName)
            .ToListAsync();

        var activeLeaseLookup = leadershipLeases
            .Where(item => item.ExpiresAt > now)
            .GroupBy(item => item.OwnerInstanceName)
            .ToDictionary(
                item => item.Key,
                item => string.Join(" | ", item.Select(lease => lease.LeaseName).OrderBy(name => name)));

        Nodes = heartbeats
            .Select(item => new NodeRowViewModel
            {
                InstanceName = item.InstanceName,
                MachineName = item.MachineName,
                EnvironmentName = item.EnvironmentName,
                RoleSummary = item.RoleSummary,
                CurrentLeases = activeLeaseLookup.GetValueOrDefault(item.InstanceName) ?? "-",
                ProcessId = item.ProcessId,
                ProcessStartedTime = item.ProcessStartedTime,
                LastSeenTime = item.LastSeenTime,
                AppVersion = item.AppVersion,
                StatusText = now - item.LastSeenTime > OfflineThreshold ? "疑似離線" : "線上中"
            })
            .ToList();

        LeadershipLeases = leadershipLeases
            .Select(item => new LeadershipLeaseRowViewModel
            {
                LeaseName = item.LeaseName,
                OwnerInstanceName = item.OwnerInstanceName,
                AcquiredAt = item.AcquiredAt,
                RenewedAt = item.RenewedAt,
                ExpiresAt = item.ExpiresAt,
                IsActive = item.ExpiresAt > now,
                ExpiresInSeconds = item.ExpiresAt <= now
                    ? 0
                    : Math.Max(0, (int)Math.Round((item.ExpiresAt - now).TotalSeconds))
            })
            .ToList();

        RecentInboxItems = await dbContext.TelegramUpdateInboxes
            .OrderByDescending(item => item.LastUpdatedTime)
            .Take(20)
            .ToListAsync();
    }

    public class NodeRowViewModel
    {
        public required string InstanceName { get; init; }
        public required string MachineName { get; init; }
        public required string EnvironmentName { get; init; }
        public required string RoleSummary { get; init; }
        public required string CurrentLeases { get; init; }
        public int ProcessId { get; init; }
        public DateTimeOffset ProcessStartedTime { get; init; }
        public DateTimeOffset LastSeenTime { get; init; }
        public string? AppVersion { get; init; }
        public required string StatusText { get; init; }
    }

    public class LeadershipLeaseRowViewModel
    {
        public required string LeaseName { get; init; }
        public required string OwnerInstanceName { get; init; }
        public DateTimeOffset AcquiredAt { get; init; }
        public DateTimeOffset RenewedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public bool IsActive { get; init; }
        public int ExpiresInSeconds { get; init; }
    }
}
