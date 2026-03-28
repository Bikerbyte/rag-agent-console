using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Pages.Games;

public class IndexModel(ApplicationDbContext dbContext, ICpblGameSyncService cpblGameSyncService) : PageModel
{
    public IReadOnlyList<GameRowViewModel> Games { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        var gameRecords = await dbContext.Games
            .OrderByDescending(game => game.GameDate)
            .ThenBy(game => game.StartTime)
            .ToListAsync();

        Games = gameRecords
            .Select(game => new GameRowViewModel
            {
                GameDate = game.GameDate,
                StartTime = game.StartTime,
                Matchup = $"{CpblTeamCatalog.GetDisplayName(game.AwayTeamCode)} vs {CpblTeamCatalog.GetDisplayName(game.HomeTeamCode)}",
                Status = game.Status switch
                {
                    "Live" => string.IsNullOrWhiteSpace(game.InningText) ? "進行中" : $"進行中 | {game.InningText}",
                    "Final" => "終場",
                    "Suspended" => "暫停或延賽",
                    _ => "尚未開打"
                },
                Score = game.AwayScore.HasValue && game.HomeScore.HasValue
                    ? $"{CpblTeamCatalog.GetDisplayName(game.AwayTeamCode)} {game.AwayScore} : {game.HomeScore} {CpblTeamCatalog.GetDisplayName(game.HomeTeamCode)}"
                    : "待開打",
                Venue = string.IsNullOrWhiteSpace(game.Venue) ? "待公告" : game.Venue,
                LastUpdatedTime = game.LastUpdatedTime
            })
            .ToList();
    }

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken cancellationToken)
    {
        var count = await cpblGameSyncService.SyncAsync(cancellationToken);
        StatusMessage = $"已完成賽程同步，更新 {count} 筆官方比賽資料。";
        return RedirectToPage();
    }

    public class GameRowViewModel
    {
        public DateOnly GameDate { get; init; }
        public TimeOnly? StartTime { get; init; }
        public required string Matchup { get; init; }
        public required string Status { get; init; }
        public required string Score { get; init; }
        public string? Venue { get; init; }
        public DateTimeOffset LastUpdatedTime { get; init; }
    }
}
