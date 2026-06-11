using System.Globalization;
using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var applicationStartedAt = DateTimeOffset.UtcNow;
var appRuntimeSection = builder.Configuration.GetSection(AppRuntimeOptions.SectionName);
var appRuntimeOptions = appRuntimeSection.Get<AppRuntimeOptions>() ?? new AppRuntimeOptions();
var telegramBotOptions = builder.Configuration.GetSection(TelegramBotOptions.SectionName).Get<TelegramBotOptions>() ?? new TelegramBotOptions();
var observabilityOptions = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();
appRuntimeOptions.InstanceName = appRuntimeOptions.GetEffectiveInstanceName();

// Pre-Build / 前置設定
// 這裡刻意保持可讀性，避免把 startup 都藏進 extension 後反而不容易維護。
builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

// Add Service Area - 共用平台服務
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys")));

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection(TelegramBotOptions.SectionName));
builder.Services.Configure<DataSourceOptions>(builder.Configuration.GetSection(DataSourceOptions.SectionName));
builder.Services.Configure<SecurityAdvisoryOptions>(builder.Configuration.GetSection(SecurityAdvisoryOptions.SectionName));
builder.Services.Configure<PushNotificationOptions>(builder.Configuration.GetSection(PushNotificationOptions.SectionName));
builder.Services.Configure<AiProviderOptions>(builder.Configuration.GetSection(AiProviderOptions.SectionName));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<VectorStoreOptions>(builder.Configuration.GetSection(VectorStoreOptions.SectionName));
builder.Services.Configure<ObservabilityOptions>(builder.Configuration.GetSection(ObservabilityOptions.SectionName));
builder.Services.Configure<AppRuntimeOptions>(appRuntimeSection);
builder.Services.PostConfigure<AppRuntimeOptions>(options => options.InstanceName = options.GetEffectiveInstanceName());
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);

if (observabilityOptions.EnableOpenTelemetry)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(observabilityOptions.ServiceName))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (observabilityOptions.EnableConsoleExporter)
            {
                tracing.AddConsoleExporter();
            }
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();

            if (observabilityOptions.EnableConsoleExporter)
            {
                metrics.AddConsoleExporter();
            }
        });
}

// Add Service Area - 資料庫
var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (string.IsNullOrWhiteSpace(defaultConnection))
    {
        options.UseInMemoryDatabase("security-advisory-bot");
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
    httpClient.BaseAddress = new Uri(SettingsUrlValidator.DefaultTelegramApiBaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<CisaKevAdvisorySource>(httpClient =>
{
    httpClient.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<NvdAdvisorySource>((serviceProvider, httpClient) =>
{
    var securityOptions = serviceProvider.GetRequiredService<IOptions<SecurityAdvisoryOptions>>().Value;
    httpClient.BaseAddress = new Uri(securityOptions.NvdApiBaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddHttpClient<IAiChatClient, AiChatClient>((serviceProvider, httpClient) =>
{
    var aiOptions = serviceProvider.GetRequiredService<IOptions<AiProviderOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.ChatTimeoutSeconds, 5, 180));
});
builder.Services.AddHttpClient<IRagEmbeddingService, RagEmbeddingService>((serviceProvider, httpClient) =>
{
    var aiOptions = serviceProvider.GetRequiredService<IOptions<AiProviderOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.EmbeddingTimeoutSeconds, 5, 180));
});
builder.Services.AddHttpClient<IOpenAiCredentialValidator, OpenAiCredentialValidator>(httpClient =>
{
    httpClient.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient();

// Add Service Area - 多語系（繁中為預設，提供 中/英 切換）
// 採用 IStringLocalizer，以「繁中原文」作為 resource key：
// 預設 zh-Hant 直接顯示 key 本身，en 文化則查 Resources/SharedResource.en.resx。
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
var supportedCultures = new[] { new CultureInfo("zh-Hant"), new CultureInfo("en") };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("zh-Hant");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.ApplyCurrentCultureToResponseHeaders = true;
});

// Add Service Area - 應用服務
builder.Services.AddRazorPages();
builder.Services.AddScoped<ITelegramPushService, TelegramPushService>();
builder.Services.AddScoped<IAppSettingsService, AppSettingsService>();
builder.Services.AddScoped<ISecurityAdvisorySource>(serviceProvider => serviceProvider.GetRequiredService<CisaKevAdvisorySource>());
builder.Services.AddScoped<ISecurityAdvisorySource>(serviceProvider => serviceProvider.GetRequiredService<NvdAdvisorySource>());
builder.Services.AddScoped<ISecurityAdvisorySyncService, SecurityAdvisorySyncService>();
builder.Services.AddSingleton<ITokenizer, MixedScriptTokenizer>();
builder.Services.AddSingleton<IBm25Index, InMemoryBm25Index>();
builder.Services.AddHostedService<Bm25IndexInitializationService>();
builder.Services.AddScoped<IRetrievalTextScorer, RetrievalTextScorer>();
builder.Services.AddScoped<EfRagVectorStore>();
builder.Services.AddScoped<PgVectorRagVectorStore>();
builder.Services.AddSingleton<IRagDomain, SecurityAdvisoryDomain>();
builder.Services.AddSingleton<IRagDomain, GenericKnowledgeDomain>();
builder.Services.AddSingleton<IRagDomainRegistry, RagDomainRegistry>();
builder.Services.AddScoped<IRagVectorStore, ConfiguredRagVectorStore>();
builder.Services.AddScoped<IRagQueryPlanner, RagQueryPlanner>();
builder.Services.AddScoped<IRagRetrievalService, RagRetrievalService>();
builder.Services.AddScoped<IRetrievalEvaluationService, RetrievalEvaluationService>();
builder.Services.AddScoped<IRagAnswerService, RagAnswerService>();
builder.Services.AddScoped<IKnowledgeDocumentTextExtractor, KnowledgeDocumentTextExtractor>();
builder.Services.AddScoped<IKnowledgeTextChunkingService, KnowledgeTextChunkingService>();
builder.Services.AddScoped<IKnowledgeDocumentIngestionService, KnowledgeDocumentIngestionService>();
builder.Services.AddScoped<ITelegramNotificationDispatchService, SecurityAdvisoryNotificationDispatchService>();
builder.Services.AddScoped<IRagAgentService, RagAgentService>();
builder.Services.AddScoped<ITelegramUpdateProcessingService, TelegramUpdateProcessingService>();
builder.Services.AddScoped<ITelegramUpdateQueueService, TelegramUpdateQueueService>();

// 背景工作角色用環境變數切開：同一個 image 可以只跑 web（ingress）或只跑 worker。
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

    // 把內建 golden set 灌成第一批可編輯的評估案例；之後就交由使用者在後台維護。
    var evaluationService = scope.ServiceProvider.GetRequiredService<IRetrievalEvaluationService>();
    await evaluationService.SeedCasesIfEmptyAsync();
}

// Razor Build / 啟動管線
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRequestLocalization();
app.UseAuthorization();

// 每台節點都回傳自己的 instance name，方便在 Nginx、瀏覽器與後台一起追 request 落點。
app.Use(async (context, next) =>
{
    context.Response.Headers["X-App-Instance"] = appRuntimeOptions.InstanceName;
    await next();
});

// 本機診斷用 endpoint
app.MapGet("/api/telegram/health", async (IAppSettingsService appSettings, CancellationToken cancellationToken) =>
{
    var options = await appSettings.GetTelegramBotOptionsAsync(cancellationToken);
    return Results.Ok(new
    {
        Provider = "Telegram",
        options.Enabled,
        HasBotToken = !string.IsNullOrWhiteSpace(options.BotToken),
        options.UseWebhookMode,
        options.WebhookPath,
        appRuntimeOptions.EnableTelegramUpdateQueueWorker
    });
});

if (appRuntimeOptions.EnableTelegramWebhookIngress)
{
    // 先提供 webhook ingress，後續若改成 queue 化，這裡會是最自然的收件入口。
    app.MapPost(telegramBotOptions.WebhookPath, async (
        HttpRequest request,
        TelegramUpdate update,
        IAppSettingsService appSettings,
        ITelegramUpdateQueueService updateQueueService,
        CancellationToken cancellationToken) =>
    {
        var currentOptions = await appSettings.GetTelegramBotOptionsAsync(cancellationToken);

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
        runtimeOptions.Value.EnableTelegramWebhookIngress,
        runtimeOptions.Value.EnableTelegramPollingWorker,
        runtimeOptions.Value.EnableTelegramUpdateQueueWorker,
        runtimeOptions.Value.EnableOfficialDataSyncWorker,
        runtimeOptions.Value.EnableNotificationWorker
    },
    Urls = app.Urls
}));

// 啟動時輸出一段 banner，方便看本機 console 或 App Service log。
app.Lifetime.ApplicationStarted.Register(() =>
{
    var addressText = app.Urls.Count == 0 ? "No bound URLs detected." : string.Join(", ", app.Urls);

    try
    {
        Console.Title = $"RAG Agent Console | PID {Environment.ProcessId}";
    }
    catch
    {
        // 只有互動式 console 才需要設定標題，失敗就略過。
    }

    app.Logger.LogInformation("============================================================");
    app.Logger.LogInformation("RAG Agent Console started");
    app.Logger.LogInformation("Instance: {InstanceName}", appRuntimeOptions.InstanceName);
    app.Logger.LogInformation("PID: {ProcessId}", Environment.ProcessId);
    app.Logger.LogInformation("URLs: {AddressText}", addressText);
    app.Logger.LogInformation(
        "Runtime => WebhookIngress: {EnableTelegramWebhookIngress}, PollingWorker: {EnableTelegramPollingWorker}, QueueWorker: {EnableTelegramUpdateQueueWorker}, OfficialSync: {EnableOfficialDataSyncWorker}, Notification: {EnableNotificationWorker}",
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

// 語言切換：寫入 culture cookie 後導回原頁。
app.MapGet("/set-culture", (string culture, string? returnUrl, HttpContext httpContext) =>
{
    httpContext.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, Path = "/" });

    return Results.LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
});

app.MapRazorPages();
app.Run();
