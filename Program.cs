using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var applicationStartedAt = DateTimeOffset.UtcNow;
var appRuntimeOptions = builder.Configuration.GetSection(AppRuntimeOptions.SectionName).Get<AppRuntimeOptions>() ?? new AppRuntimeOptions();
var telegramBotOptions = builder.Configuration.GetSection(TelegramBotOptions.SectionName).Get<TelegramBotOptions>() ?? new TelegramBotOptions();
appRuntimeOptions.InstanceName = appRuntimeOptions.GetEffectiveInstanceName();

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
builder.Services.Configure<AppRuntimeOptions>(builder.Configuration.GetSection(AppRuntimeOptions.SectionName));
builder.Services.PostConfigure<AppRuntimeOptions>(options =>
{
    options.InstanceName = options.GetEffectiveInstanceName();
});
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
builder.Services.AddScoped<IRuntimeLeadershipLeaseService, RuntimeLeadershipLeaseService>();
builder.Services.AddScoped<ITelegramUpdateProcessingService, TelegramUpdateProcessingService>();
builder.Services.AddScoped<ITelegramUpdateQueueService, TelegramUpdateQueueService>();
builder.Services.AddHostedService<RuntimeNodeHeartbeatBackgroundService>();

// 背景工作角色先用設定切開，之後部署到多台 VM 時比較不會互相撞工作。
if (appRuntimeOptions.EnableTelegramWebhookIngress && telegramBotOptions.UseWebhookMode)
{
    builder.Services.AddHostedService<TelegramWebhookRegistrationBackgroundService>();
}

if (appRuntimeOptions.EnableTelegramPollingWorker)
{
    builder.Services.AddHostedService<TelegramPollingBackgroundService>();
}

if (appRuntimeOptions.EnableTelegramUpdateQueueWorker)
{
    builder.Services.AddHostedService<TelegramUpdateQueueBackgroundService>();
}

if (appRuntimeOptions.EnableOfficialDataSyncWorker)
{
    builder.Services.AddHostedService<OfficialDataSyncBackgroundService>();
}

if (appRuntimeOptions.EnableNotificationWorker)
{
    builder.Services.AddHostedService<TelegramNotificationBackgroundService>();
}

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

// 每台節點都回傳自己的 instance name，方便在 Nginx、瀏覽器與後台一起追 request 落點。
app.Use(async (context, next) =>
{
    context.Response.Headers["X-App-Instance"] = appRuntimeOptions.InstanceName;
    await next();
});

// 本機診斷用 endpoint
app.MapGet("/api/telegram/health", (IOptions<TelegramBotOptions> options) => Results.Ok(new
{
    Provider = "Telegram",
    options.Value.Enabled,
    HasBotToken = !string.IsNullOrWhiteSpace(options.Value.BotToken),
    options.Value.UseWebhookMode,
    options.Value.WebhookPath,
    appRuntimeOptions.EnableTelegramUpdateQueueWorker
}));

if (appRuntimeOptions.EnableTelegramWebhookIngress)
{
    // 先提供 webhook ingress，後續若改成 queue 化，這裡會是最自然的收件入口。
    app.MapPost(telegramBotOptions.WebhookPath, async (
        HttpRequest request,
        TelegramUpdate update,
        IOptions<TelegramBotOptions> options,
        ITelegramUpdateQueueService updateQueueService,
        CancellationToken cancellationToken) =>
    {
        var currentOptions = options.Value;

        if (!currentOptions.Enabled || string.IsNullOrWhiteSpace(currentOptions.BotToken))
        {
            return Results.Ok(new
            {
                Accepted = false,
                Reason = "Telegram bot disabled."
            });
        }

        if (!string.IsNullOrWhiteSpace(currentOptions.WebhookSecretToken))
        {
            var providedSecretToken = request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            if (!string.Equals(providedSecretToken, currentOptions.WebhookSecretToken, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }
        }

        await updateQueueService.EnqueueAsync(update, "Webhook", cancellationToken);
        return Results.Ok(new
        {
            Accepted = true
        });
    });
}

app.MapGet("/api/runtime", (IHostEnvironment environment, IOptions<AppRuntimeOptions> runtimeOptions) => Results.Ok(new
{
    ProcessId = Environment.ProcessId,
    InstanceName = runtimeOptions.Value.InstanceName,
    Environment = environment.EnvironmentName,
    StartedAt = applicationStartedAt,
    Runtime = new
    {
        runtimeOptions.Value.EnableLeadershipLease,
        runtimeOptions.Value.LeaseDurationSeconds,
        runtimeOptions.Value.LeaseRenewIntervalSeconds,
        runtimeOptions.Value.LeaseAcquireRetrySeconds,
        runtimeOptions.Value.EnableTelegramWebhookIngress,
        runtimeOptions.Value.EnableTelegramPollingWorker,
        runtimeOptions.Value.EnableTelegramUpdateQueueWorker,
        runtimeOptions.Value.EnableOfficialDataSyncWorker,
        runtimeOptions.Value.EnableNotificationWorker
    },
    Urls = app.Urls
}));

app.MapGet("/api/runtime/leases", async (ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
{
    var now = DateTimeOffset.UtcNow;
    var leases = await dbContext.RuntimeLeadershipLeases
        .OrderBy(item => item.LeaseName)
        .Select(item => new
        {
            item.LeaseName,
            item.OwnerInstanceName,
            item.AcquiredAt,
            item.RenewedAt,
            item.ExpiresAt,
            IsActive = item.ExpiresAt > now,
            ExpiresInSeconds = item.ExpiresAt <= now
                ? 0
                : Math.Max(0, (int)Math.Round((item.ExpiresAt - now).TotalSeconds))
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(new
    {
        ServerTime = now,
        Count = leases.Count,
        Leases = leases
    });
});

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
    app.Logger.LogInformation("Instance: {InstanceName}", appRuntimeOptions.InstanceName);
    app.Logger.LogInformation("PID: {ProcessId}", Environment.ProcessId);
    app.Logger.LogInformation("URLs: {AddressText}", addressText);
    app.Logger.LogInformation(
        "Runtime => LeadershipLease: {EnableLeadershipLease} (Duration: {LeaseDurationSeconds}s, Renew: {LeaseRenewIntervalSeconds}s, Retry: {LeaseAcquireRetrySeconds}s), WebhookIngress: {EnableTelegramWebhookIngress}, Polling: {EnableTelegramPollingWorker}, UpdateQueueWorker: {EnableTelegramUpdateQueueWorker}, OfficialSync: {EnableOfficialDataSyncWorker}, Notification: {EnableNotificationWorker}",
        appRuntimeOptions.EnableLeadershipLease,
        appRuntimeOptions.LeaseDurationSeconds,
        appRuntimeOptions.LeaseRenewIntervalSeconds,
        appRuntimeOptions.LeaseAcquireRetrySeconds,
        appRuntimeOptions.EnableTelegramWebhookIngress,
        appRuntimeOptions.EnableTelegramPollingWorker,
        appRuntimeOptions.EnableTelegramUpdateQueueWorker,
        appRuntimeOptions.EnableOfficialDataSyncWorker,
        appRuntimeOptions.EnableNotificationWorker);
    app.Logger.LogInformation(
        "Telegram => UseWebhookMode: {UseWebhookMode}, WebhookPath: {WebhookPath}",
        telegramBotOptions.UseWebhookMode,
        telegramBotOptions.WebhookPath);
    if (!appRuntimeOptions.EnableTelegramUpdateQueueWorker &&
        (appRuntimeOptions.EnableTelegramWebhookIngress || appRuntimeOptions.EnableTelegramPollingWorker))
    {
        app.Logger.LogWarning("Telegram ingress is enabled, but TelegramUpdateQueueWorker is disabled. Updates may queue without being processed.");
    }
    app.Logger.LogInformation("Use Run-Local.cmd to keep a visible console.");
    app.Logger.LogInformation("Use Status-Local.cmd or Stop-Local.cmd if the port stays occupied.");
    app.Logger.LogInformation("============================================================");
});

app.MapRazorPages();
app.Run();
