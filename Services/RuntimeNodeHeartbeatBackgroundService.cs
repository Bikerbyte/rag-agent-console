using System.Reflection;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 定期回報目前節點的執行狀態，讓後台可以集中看到多節點清單。
/// </summary>
public class RuntimeNodeHeartbeatBackgroundService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment hostEnvironment,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<RuntimeNodeHeartbeatBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private readonly DateTimeOffset processStartedTime = DateTimeOffset.UtcNow;
    private readonly string appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var now = DateTimeOffset.UtcNow;
                var instanceName = runtimeOptions.Value.GetEffectiveInstanceName();

                var heartbeat = await dbContext.RuntimeNodeHeartbeats
                    .FirstOrDefaultAsync(item => item.InstanceName == instanceName, stoppingToken);

                if (heartbeat is null)
                {
                    heartbeat = new RuntimeNodeHeartbeat
                    {
                        InstanceName = instanceName,
                        MachineName = Environment.MachineName,
                        EnvironmentName = hostEnvironment.EnvironmentName,
                        RoleSummary = runtimeOptions.Value.BuildRoleSummary(),
                        Status = "Online",
                        ProcessId = Environment.ProcessId,
                        ProcessStartedTime = processStartedTime,
                        LastSeenTime = now,
                        AppVersion = appVersion
                    };

                    dbContext.RuntimeNodeHeartbeats.Add(heartbeat);
                }
                else
                {
                    heartbeat.MachineName = Environment.MachineName;
                    heartbeat.EnvironmentName = hostEnvironment.EnvironmentName;
                    heartbeat.RoleSummary = runtimeOptions.Value.BuildRoleSummary();
                    heartbeat.Status = "Online";
                    heartbeat.ProcessId = Environment.ProcessId;
                    heartbeat.ProcessStartedTime = processStartedTime;
                    heartbeat.LastSeenTime = now;
                    heartbeat.AppVersion = appVersion;
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Runtime node heartbeat update failed. Check whether stored runtime timestamps are UTC and AppRuntime:InstanceName is unique across nodes.");
            }

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }

}
