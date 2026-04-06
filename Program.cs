using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var applicationStartedAt = DateTimeOffset.Now;

// Pre-Build / 前置設定
// 這裡刻意保持可讀性，避免把 startup 都藏進 extension 後反而不容易維護。
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add Service Area - 共用平台服務
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys")));

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection(TelegramBotOptions.SectionName));
builder.Services.Configure<DataSourceOptions>(builder.Configuration.GetSection(DataSourceOptions.SectionName));
builder.Services.Configure<PushNotificationOptions>(builder.Configuration.GetSection(PushNotificationOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);

// Add Service Area - 資料庫
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (string.IsNullOrWhiteSpace(defaultConnection))
    {
        options.UseInMemoryDatabase("cpbl-telegram-assistant");
        return;
    }

    if (defaultConnection.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
        defaultConnection.Contains("Username=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(defaultConnection);
        return;
    }

    options.UseSqlServer(defaultConnection);
});

// Add Service Area - 對外 HTTP Client
builder.Services.AddHttpClient<ITelegramBotClient, TelegramBotClient>((serviceProvider, httpClient) =>
{
    var telegramBotOptions = serviceProvider.GetRequiredService<IOptions<TelegramBotOptions>>().Value;
    httpClient.BaseAddress = new Uri(telegramBotOptions.ApiBaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient();

// Add Service Area - 應用服務
builder.Services.AddRazorPages();
builder.Services.AddScoped<ICpblGameSyncService, CpblGameSyncService>();
builder.Services.AddScoped<IBaseballNewsSyncService, BaseballNewsSyncService>();
builder.Services.AddScoped<ICpblOfficialDataClient, CpblOfficialDataClient>();
builder.Services.AddScoped<ICpblInsightService, CpblInsightService>();
builder.Services.AddScoped<ITelegramPushService, TelegramPushService>();
builder.Services.AddScoped<ITelegramNotificationDispatchService, TelegramNotificationDispatchService>();
builder.Services.AddScoped<ICommandReplyService, CommandReplyService>();
builder.Services.AddHostedService<TelegramPollingBackgroundService>();
builder.Services.AddHostedService<OfficialDataSyncBackgroundService>();
builder.Services.AddHostedService<TelegramNotificationBackgroundService>();

var app = builder.Build();

// Data Prep / 資料準備
// 在第一個 request 進來前，先把本機資料夾和 seed data 準備好。
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data", "DataProtectionKeys"));

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    if (dbContext.Database.IsRelational())
    {
        await dbContext.Database.MigrateAsync();
    }
    else
    {
        await dbContext.Database.EnsureCreatedAsync();
    }

    await DemoDataSeeder.SeedAsync(dbContext);
}

// Razor Build / 啟動管線
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// 本機診斷用 endpoint
app.MapGet("/api/telegram/health", (IOptions<TelegramBotOptions> options) => Results.Ok(new
{
    Provider = "Telegram",
    options.Value.Enabled,
    HasBotToken = !string.IsNullOrWhiteSpace(options.Value.BotToken)
}));

app.MapGet("/api/runtime", (IHostEnvironment environment) => Results.Ok(new
{
    ProcessId = Environment.ProcessId,
    Environment = environment.EnvironmentName,
    StartedAt = applicationStartedAt,
    Urls = app.Urls
}));

// 啟動時輸出一段 banner，方便看本機 console 或 App Service log。
app.Lifetime.ApplicationStarted.Register(() =>
{
    var addressText = app.Urls.Count == 0 ? "No bound URLs detected." : string.Join(", ", app.Urls);

    try
    {
        Console.Title = $"CPBL Telegram Assistant | PID {Environment.ProcessId}";
    }
    catch
    {
        // 只有互動式 console 才需要設定標題，失敗就略過。
    }

    app.Logger.LogInformation("============================================================");
    app.Logger.LogInformation("CPBL Telegram Assistant started");
    app.Logger.LogInformation("PID: {ProcessId}", Environment.ProcessId);
    app.Logger.LogInformation("URLs: {AddressText}", addressText);
    app.Logger.LogInformation("Use Run-Local.cmd to keep a visible console.");
    app.Logger.LogInformation("Use Status-Local.cmd or Stop-Local.cmd if the port stays occupied.");
    app.Logger.LogInformation("============================================================");
});

app.MapRazorPages();
app.Run();
