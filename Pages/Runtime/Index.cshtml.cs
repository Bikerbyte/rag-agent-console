using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Pages.Runtime;

public class IndexModel(ApplicationDbContext dbContext) : PageModel
{
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromSeconds(45);

    public IReadOnlyList<NodeRowViewModel> Nodes { get; private set; } = [];
    public IReadOnlyList<TelegramUpdateInbox> RecentInboxItems { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var now = DateTimeOffset.UtcNow;

        var heartbeats = await dbContext.RuntimeNodeHeartbeats
            .OrderByDescending(item => item.LastSeenTime)
            .ToListAsync();

        Nodes = heartbeats
            .Select(item => new NodeRowViewModel
            {
                InstanceName = item.InstanceName,
                MachineName = item.MachineName,
                EnvironmentName = item.EnvironmentName,
                RoleSummary = item.RoleSummary,
                ProcessId = item.ProcessId,
                ProcessStartedTime = item.ProcessStartedTime,
                LastSeenTime = item.LastSeenTime,
                AppVersion = item.AppVersion,
                StatusText = now - item.LastSeenTime > OfflineThreshold ? "疑似離線" : "在線中"
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
        public int ProcessId { get; init; }
        public DateTimeOffset ProcessStartedTime { get; init; }
        public DateTimeOffset LastSeenTime { get; init; }
        public string? AppVersion { get; init; }
        public required string StatusText { get; init; }
    }
}
