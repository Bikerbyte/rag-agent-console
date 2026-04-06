using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CPBLLineBotCloud.Tests;

public class RuntimeLeadershipLeaseTests
{
    [Fact]
    public async Task TryAcquireOrRenewAsync_WhenLeaseDoesNotExist_AcquiresLease()
    {
        await using var connection = CreateOpenConnection();
        await EnsureCreatedAsync(connection);
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero));

        await using var dbContext = CreateSqliteDbContext(connection);
        var service = CreateLeaseService(dbContext, timeProvider, "node-a");

        var result = await service.TryAcquireOrRenewAsync("OfficialDataSync");

        Assert.True(result.IsLeader);
        Assert.Equal("Acquired", result.Action);
        Assert.Equal("node-a", result.CurrentOwnerInstanceName);
    }

    [Fact]
    public async Task TryAcquireOrRenewAsync_WhenOwnerIsCurrentNode_RenewsLease()
    {
        await using var connection = CreateOpenConnection();
        await EnsureCreatedAsync(connection);
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero));

        await using var dbContext = CreateSqliteDbContext(connection);
        var service = CreateLeaseService(dbContext, timeProvider, "node-a");
        var firstResult = await service.TryAcquireOrRenewAsync("OfficialDataSync");

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var secondResult = await service.TryAcquireOrRenewAsync("OfficialDataSync");

        Assert.True(secondResult.IsLeader);
        Assert.Equal("Renewed", secondResult.Action);
        Assert.True(secondResult.ExpiresAt > firstResult.ExpiresAt);
    }

    [Fact]
    public async Task TryAcquireOrRenewAsync_WhenLeaseIsActiveAndOwnedByAnotherNode_FailsAcquire()
    {
        await using var connection = CreateOpenConnection();
        await EnsureCreatedAsync(connection);
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero));

        await using (var ownerContext = CreateSqliteDbContext(connection))
        {
            var ownerService = CreateLeaseService(ownerContext, timeProvider, "node-a");
            await ownerService.TryAcquireOrRenewAsync("OfficialDataSync");
        }

        await using var contenderContext = CreateSqliteDbContext(connection);
        var contenderService = CreateLeaseService(contenderContext, timeProvider, "node-b");

        var result = await contenderService.TryAcquireOrRenewAsync("OfficialDataSync");

        Assert.False(result.IsLeader);
        Assert.Equal("Rejected", result.Action);
        Assert.Equal("node-a", result.CurrentOwnerInstanceName);
    }

    [Fact]
    public async Task TryAcquireOrRenewAsync_WhenLeaseExpires_NewNodeCanTakeOver()
    {
        await using var connection = CreateOpenConnection();
        await EnsureCreatedAsync(connection);
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero));

        await using (var ownerContext = CreateSqliteDbContext(connection))
        {
            var ownerService = CreateLeaseService(ownerContext, timeProvider, "node-a");
            await ownerService.TryAcquireOrRenewAsync("OfficialDataSync");
        }

        timeProvider.Advance(TimeSpan.FromSeconds(35));

        await using var contenderContext = CreateSqliteDbContext(connection);
        var contenderService = CreateLeaseService(contenderContext, timeProvider, "node-b");

        var result = await contenderService.TryAcquireOrRenewAsync("OfficialDataSync");

        Assert.True(result.IsLeader);
        Assert.Equal("TakenOver", result.Action);
        Assert.Equal("node-b", result.CurrentOwnerInstanceName);
    }

    [Fact]
    public async Task TryAcquireOrRenewAsync_WhenTwoNodesRaceForExpiredLease_OnlyOneSucceeds()
    {
        await using var connection = CreateOpenConnection();
        await EnsureCreatedAsync(connection);
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero));

        await using (var ownerContext = CreateSqliteDbContext(connection))
        {
            var ownerService = CreateLeaseService(ownerContext, timeProvider, "node-a");
            await ownerService.TryAcquireOrRenewAsync("OfficialDataSync");
        }

        timeProvider.Advance(TimeSpan.FromSeconds(35));

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var contenderTaskA = Task.Run(async () =>
        {
            await startGate.Task;
            await using var dbContext = CreateSqliteDbContext(connection);
            var service = CreateLeaseService(dbContext, timeProvider, "node-b");
            return await service.TryAcquireOrRenewAsync("OfficialDataSync");
        });

        var contenderTaskB = Task.Run(async () =>
        {
            await startGate.Task;
            await using var dbContext = CreateSqliteDbContext(connection);
            var service = CreateLeaseService(dbContext, timeProvider, "node-c");
            return await service.TryAcquireOrRenewAsync("OfficialDataSync");
        });

        startGate.SetResult();
        var results = await Task.WhenAll(contenderTaskA, contenderTaskB);

        Assert.Equal(1, results.Count(item => item.IsLeader));
    }

    [Fact]
    public async Task OfficialDataSyncBackgroundService_WhenLeaseIsNotOwned_DoesNotExecuteWork()
    {
        using var hostCancellationSource = new CancellationTokenSource();
        var serviceProvider = BuildScopedServiceProvider(new StubRuntimeLeadershipLeaseService(
            new RuntimeLeadershipLeaseResult
            {
                LeaseName = "OfficialDataSync",
                InstanceName = "node-b",
                CurrentOwnerInstanceName = "node-a",
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30),
                IsLeader = false,
                Action = "Rejected"
            }));

        var service = new TestOfficialDataSyncBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new AppRuntimeOptions
            {
                InstanceName = "node-b",
                EnableLeadershipLease = true,
                LeaseDurationSeconds = 30,
                LeaseRenewIntervalSeconds = 10,
                LeaseAcquireRetrySeconds = 1
            }),
            Options.Create(new DataSourceOptions
            {
                AutoSyncIntervalMinutes = 5
            }),
            NullLogger<OfficialDataSyncBackgroundService>.Instance,
            hostCancellationSource);

        await service.StartAsync(hostCancellationSource.Token);
        await service.WaitForLoopAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, service.ExecuteOwnedWorkCallCount);
    }

    [Fact]
    public async Task TelegramNotificationBackgroundService_WhenLeaseIsNotOwned_DoesNotExecuteWork()
    {
        using var hostCancellationSource = new CancellationTokenSource();
        var serviceProvider = BuildScopedServiceProvider(new StubRuntimeLeadershipLeaseService(
            new RuntimeLeadershipLeaseResult
            {
                LeaseName = "TelegramNotification",
                InstanceName = "node-b",
                CurrentOwnerInstanceName = "node-a",
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30),
                IsLeader = false,
                Action = "Rejected"
            }));

        var service = new TestTelegramNotificationBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new AppRuntimeOptions
            {
                InstanceName = "node-b",
                EnableLeadershipLease = true,
                LeaseDurationSeconds = 30,
                LeaseRenewIntervalSeconds = 10,
                LeaseAcquireRetrySeconds = 1
            }),
            Options.Create(new PushNotificationOptions
            {
                WorkerIntervalSeconds = 30
            }),
            NullLogger<TelegramNotificationBackgroundService>.Instance,
            hostCancellationSource);

        await service.StartAsync(hostCancellationSource.Token);
        await service.WaitForLoopAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, service.ExecuteOwnedWorkCallCount);
    }

    private static RuntimeLeadershipLeaseService CreateLeaseService(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        string instanceName)
    {
        return new RuntimeLeadershipLeaseService(
            dbContext,
            timeProvider,
            Options.Create(new AppRuntimeOptions
            {
                InstanceName = instanceName,
                EnableLeadershipLease = true,
                LeaseDurationSeconds = 30,
                LeaseRenewIntervalSeconds = 10,
                LeaseAcquireRetrySeconds = 5
            }),
            NullLogger<RuntimeLeadershipLeaseService>.Instance);
    }

    private static SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static async Task EnsureCreatedAsync(SqliteConnection connection)
    {
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ServiceProvider BuildScopedServiceProvider(IRuntimeLeadershipLeaseService leaseService)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRuntimeLeadershipLeaseService>(_ => leaseService);
        return services.BuildServiceProvider();
    }

    private sealed class MutableTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset current = currentTime;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration)
        {
            current = current.Add(duration);
        }
    }

    private sealed class StubRuntimeLeadershipLeaseService(RuntimeLeadershipLeaseResult result) : IRuntimeLeadershipLeaseService
    {
        public Task<RuntimeLeadershipLeaseResult> TryAcquireOrRenewAsync(string leaseName, CancellationToken cancellationToken = default)
            => Task.FromResult(result);

        public Task<bool> IsCurrentLeaderAsync(string leaseName, CancellationToken cancellationToken = default)
            => Task.FromResult(result.IsLeader);

        public Task ReleaseIfOwnedAsync(string leaseName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RuntimeLeadershipLease>> GetActiveLeasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RuntimeLeadershipLease>>([]);
    }

    private sealed class TestOfficialDataSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<AppRuntimeOptions> runtimeOptions,
        IOptions<DataSourceOptions> dataSourceOptions,
        ILogger<OfficialDataSyncBackgroundService> logger,
        CancellationTokenSource hostCancellationSource)
        : OfficialDataSyncBackgroundService(scopeFactory, timeProvider, runtimeOptions, dataSourceOptions, logger)
    {
        private readonly TaskCompletionSource loopCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int delayCount;

        public int ExecuteOwnedWorkCallCount { get; private set; }

        public Task WaitForLoopAsync() => loopCompleted.Task;

        protected override TimeSpan GetStartupDelay() => TimeSpan.Zero;

        protected override Task ExecuteOwnedWorkAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ExecuteOwnedWorkCallCount++;
            return Task.CompletedTask;
        }

        protected override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref delayCount) >= 2)
            {
                loopCompleted.TrySetResult();
                hostCancellationSource.Cancel();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestTelegramNotificationBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<AppRuntimeOptions> runtimeOptions,
        IOptions<PushNotificationOptions> pushOptions,
        ILogger<TelegramNotificationBackgroundService> logger,
        CancellationTokenSource hostCancellationSource)
        : TelegramNotificationBackgroundService(scopeFactory, timeProvider, runtimeOptions, pushOptions, logger)
    {
        private readonly TaskCompletionSource loopCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int delayCount;

        public int ExecuteOwnedWorkCallCount { get; private set; }

        public Task WaitForLoopAsync() => loopCompleted.Task;

        protected override TimeSpan GetStartupDelay() => TimeSpan.Zero;

        protected override Task ExecuteOwnedWorkAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ExecuteOwnedWorkCallCount++;
            return Task.CompletedTask;
        }

        protected override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref delayCount) >= 2)
            {
                loopCompleted.TrySetResult();
                hostCancellationSource.Cancel();
            }

            return Task.CompletedTask;
        }
    }
}
