using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var applicationStartedAt = DateTimeOffset.Now;

// Pre-Build
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys")));

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection(TelegramBotOptions.SectionName));
builder.Services.Configure<DataSourceOptions>(builder.Configuration.GetSection(DataSourceOptions.SectionName));
builder.Services.Configure<PushNotificationOptions>(builder.Configuration.GetSection(PushNotificationOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);

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

builder.Services.AddHttpClient<ITelegramBotClient, TelegramBotClient>((serviceProvider, httpClient) =>
{
    var telegramBotOptions = serviceProvider.GetRequiredService<IOptions<TelegramBotOptions>>().Value;
    httpClient.BaseAddress = new Uri(telegramBotOptions.ApiBaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient();

// Add Service Area
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

// Data Prep
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

// Razor Build
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

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

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addressText = app.Urls.Count == 0 ? "No bound URLs detected." : string.Join(", ", app.Urls);

    try
    {
        Console.Title = $"CPBL Telegram Assistant | PID {Environment.ProcessId}";
    }
    catch
    {
        // Best effort only for interactive console sessions.
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
