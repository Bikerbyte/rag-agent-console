using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CPBLLineBotCloud.Tests;

public class AppRuntimeOptionsExtensionsTests
{
    [Fact]
    public void ApplyRuntimeProfile_WhenProfileIsStandard_AppliesRecommendedDefaults()
    {
        var configuration = CreateConfiguration();
        var options = new AppRuntimeOptions
        {
            Profile = AppRuntimeProfiles.Standard,
            EnableTelegramPollingWorker = true
        };

        options.ApplyRuntimeProfile(configuration);

        Assert.Equal(AppRuntimeProfiles.Standard, options.Profile);
        Assert.True(options.EnableTelegramWebhookIngress);
        Assert.False(options.EnableTelegramPollingWorker);
        Assert.True(options.EnableTelegramUpdateQueueWorker);
        Assert.True(options.EnableOfficialDataSyncWorker);
        Assert.True(options.EnableNotificationWorker);
    }

    [Fact]
    public void ApplyRuntimeProfile_WhenProfileIsWorkerOnly_DisablesIngressRoles()
    {
        var configuration = CreateConfiguration();
        var options = new AppRuntimeOptions
        {
            Profile = AppRuntimeProfiles.WorkerOnly,
            EnableTelegramWebhookIngress = true
        };

        options.ApplyRuntimeProfile(configuration);

        Assert.False(options.EnableTelegramWebhookIngress);
        Assert.False(options.EnableTelegramPollingWorker);
        Assert.True(options.EnableTelegramUpdateQueueWorker);
        Assert.True(options.EnableOfficialDataSyncWorker);
        Assert.True(options.EnableNotificationWorker);
    }

    [Fact]
    public void ApplyRuntimeProfile_WhenExplicitOverrideExists_KeepsOverrideValue()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            [$"{AppRuntimeOptions.SectionName}:{nameof(AppRuntimeOptions.EnableNotificationWorker)}"] = "false"
        });

        var options = new AppRuntimeOptions
        {
            Profile = AppRuntimeProfiles.Standard,
            EnableNotificationWorker = false
        };

        options.ApplyRuntimeProfile(configuration);

        Assert.False(options.EnableNotificationWorker);
    }

    [Fact]
    public void ApplyRuntimeProfile_WhenOverrideIsBlank_StillUsesProfileDefaults()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            [$"{AppRuntimeOptions.SectionName}:{nameof(AppRuntimeOptions.EnableTelegramPollingWorker)}"] = string.Empty
        });

        var options = new AppRuntimeOptions
        {
            Profile = AppRuntimeProfiles.Standard,
            EnableTelegramPollingWorker = true
        };

        options.ApplyRuntimeProfile(configuration);

        Assert.False(options.EnableTelegramPollingWorker);
    }

    [Fact]
    public void ApplyRuntimeProfile_WhenProfileIsUnknown_FallsBackToCustom()
    {
        var configuration = CreateConfiguration();
        var options = new AppRuntimeOptions
        {
            Profile = "SomethingElse",
            EnableTelegramWebhookIngress = false,
            EnableTelegramPollingWorker = true,
            EnableTelegramUpdateQueueWorker = false,
            EnableOfficialDataSyncWorker = false,
            EnableNotificationWorker = true
        };

        options.ApplyRuntimeProfile(configuration);

        Assert.Equal(AppRuntimeProfiles.Custom, options.Profile);
        Assert.False(options.EnableTelegramWebhookIngress);
        Assert.True(options.EnableTelegramPollingWorker);
        Assert.False(options.EnableTelegramUpdateQueueWorker);
        Assert.False(options.EnableOfficialDataSyncWorker);
        Assert.True(options.EnableNotificationWorker);
    }

    private static IConfiguration CreateConfiguration(IDictionary<string, string?>? values = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

        return configuration.GetSection(AppRuntimeOptions.SectionName);
    }
}
