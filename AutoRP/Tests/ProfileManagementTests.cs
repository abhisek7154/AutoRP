using AutoRP.Configuration;
using AutoRP.Models;
using AutoRP.Services;
using AutoRP.ViewModels;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AutoRP.Tests;

public sealed class ProfileManagementTests
{
    [Fact]
    public void AddsEditsDeletesAndPersistsProfile()
    {
        using var context = TestContext.Create();
        var profile = Profile("Test Profile", "test.exe", "Initial details");

        context.Configuration.AddProfile(profile);
        Assert.Single(context.Configuration.Profiles);

        context.Configuration.UpdateProfile(profile.Name, profile with { Details = "Updated details" });
        Assert.Equal("Updated details", context.Configuration.Profiles.Single().Details);

        Assert.True(context.Configuration.DeleteProfile(profile.Name));
        Assert.Empty(context.Configuration.Profiles);
        Assert.Empty(new JsonProfileConfigurationService(context.Options, context.Logger, context.Path).Profiles);
    }

    [Fact]
    public void DisabledProfileIsIgnoredAndCanBeEnabledWithoutRestart()
    {
        using var context = TestContext.Create();
        var profile = Profile("Disabled", "disabled.exe", "Disabled details") with { IsEnabled = false };
        context.Configuration.AddProfile(profile);
        using var manager = new PresenceProfileManager(context.Configuration);
        var application = new ActiveApplication(1, "disabled", "disabled.exe", "Disabled");

        Assert.Null(manager.FindProfile(application));
        context.Configuration.SetProfileEnabled(profile.Name, true);

        Assert.Equal(profile.Name, manager.FindProfile(application)?.Name);
    }

    [Fact]
    public void EditedProfileIsUsedWithoutRestart()
    {
        using var context = TestContext.Create();
        var profile = Profile("Editable", "editable.exe", "Before");
        context.Configuration.AddProfile(profile);
        using var manager = new PresenceProfileManager(context.Configuration);
        var application = new ActiveApplication(1, "editable", "editable.exe", "Editable");

        context.Configuration.UpdateProfile(profile.Name, profile with { Details = "After" });

        Assert.Equal("After", manager.FindProfile(application)?.Details);
    }

    [Fact]
    public async Task TestPresenceUsesSelectedProfile()
    {
        using var context = TestContext.Create();
        var profile = Profile("Testable", "testable.exe", "Send this");
        context.Configuration.AddProfile(profile);
        using var manager = new PresenceProfileManager(context.Configuration);
        var discord = new RecordingDiscordService();
        using var viewModel = new ProfileManagementViewModel(context.Configuration, manager, discord);
        viewModel.SelectedProfile = viewModel.Profiles.Single();

        await viewModel.TestSelectedProfileAsync();

        Assert.Equal(profile.Name, discord.LastProfile?.Name);
    }

    private static RpcProfile Profile(string name, string processName, string details)
    {
        return new RpcProfile(processName, details, "Testing") { Name = name };
    }

    private sealed class TestContext : IDisposable
    {
        public required ILoggerFactory LoggerFactory { get; init; }
        public required string Path { get; init; }
        public required AutoRpOptions Options { get; init; }
        public required JsonProfileConfigurationService Configuration { get; init; }
        public required ILogger<JsonProfileConfigurationService> Logger { get; init; }

        public static TestContext Create()
        {
            var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { });
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"autor p-{Guid.NewGuid():N}.json");
            var options = new AutoRpOptions { Profiles = [] };
            var logger = loggerFactory.CreateLogger<JsonProfileConfigurationService>();
            return new TestContext
            {
                LoggerFactory = loggerFactory,
                Path = path,
                Options = options,
                Logger = logger,
                Configuration = new JsonProfileConfigurationService(options, logger, path)
            };
        }

        public void Dispose()
        {
            LoggerFactory.Dispose();
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }

    private sealed class RecordingDiscordService : IDiscordRpcService
    {
        public event EventHandler<DiscordConnectionStatusChangedEventArgs>? ConnectionStatusChanged
        {
            add { }
            remove { }
        }
        public bool IsConnected => true;
        public RpcProfile? LastProfile { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPresenceAsync(RpcProfile profile, CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            return Task.CompletedTask;
        }
        public Task ClearPresenceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}