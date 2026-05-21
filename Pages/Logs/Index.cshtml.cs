using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace SecurityAdvisoryBot.Pages.Logs;

public class IndexModel(ApplicationDbContext dbContext) : PageModel
{
    public IReadOnlyList<PushLog> PushLogs { get; private set; } = [];
    public IReadOnlyList<SyncJobLog> SyncJobLogs { get; private set; } = [];

    public async Task OnGetAsync()
    {
        PushLogs = await dbContext.PushLogs
            .OrderByDescending(log => log.CreatedTime)
            .Take(20)
            .ToListAsync();

        SyncJobLogs = await dbContext.SyncJobLogs
            .OrderByDescending(log => log.StartTime)
            .Take(20)
            .ToListAsync();
    }
}
