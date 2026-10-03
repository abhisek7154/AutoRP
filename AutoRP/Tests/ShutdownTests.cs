using AutoRP.Models;
using AutoRP.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AutoRP.Tests;

public sealed class ShutdownTests
{
    [Fact]
    public async Task StopDisconnectsDiscordStopsMonitoringAndIsSafeToRepeat()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var activeWindow = new FakeActiveWindowService();
        var discord = new FakeDiscordRpcService();
        var profileManager = new InMemoryProfileManager();
        using var autoSwitch = new AutoSwitchService(
            activeWindow,
            profileManager,
            discord,
            loggerFactory.CreateLogger<AutoSwitchService>());
        var settingsPath = Path.Combine(Path.GetTempPath(), $"autor p-shutdown-{Guid.NewGuid():N}.json");
        var settings = new JsonSettingsService(
            loggerFactory.CreateLogger<JsonSettingsService>(),
            settingsPath);
        var coordinator = new PresenceCoordinator(
            discord,
            autoSwitch,
            settings,
            new Configuration.AutoRpOptions { DiscordApplicationId = string.Empty },
            loggerFactory.CreateLogger<PresenceCoordinator>());

        await coordinator.StartAsync();
        await coordinator.StopAsync();
        await coordinator.StopAsync();
        coordinator.Dispose();
        coordinator.Dispose();

        activeWindow.Raise(new ActiveApplication(1, "after-stop", "after-stop.exe", "After stop"));

        Assert.Equal(1, activeWindow.StartCalls);
        Assert.Equal(1, activeWindow.StopCalls);
        Assert.Equal(1, discord.DisconnectCalls);
        Assert.Empty(discord.SetCalls);

        if (File.Exists(settingsPath))
        {
            File.Delete(settingsPath);
        }
    }

    private sealed class FakeActiveWindowService : IActiveWindowService
    {
        public event EventHandler<ActiveApplicationChangedEventArgs>? ApplicationChanged;
        public ActiveApplication? CurrentApplication => null;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public ActiveApplication? GetActiveApplication() => null;
        public void Start() => StartCalls++;
        public void Stop() => StopCalls++;

        public void Raise(ActiveApplication application)
        {
            ApplicationChanged?.Invoke(this, new ActiveApplicationChangedEventArgs(null, application));
        }
    }

    private sealed class InMemoryProfileManager : IPresenceProfileManager
    {
        public event EventHandler? ProfilesChanged
        {
            add { }
            remove { }
        }
        public IReadOnlyList<RpcProfile> Profiles => [];
        public RpcProfile? FindProfile(ActiveApplication? application) => null;
    }

    private sealed class FakeDiscordRpcService : IDiscordRpcService
    {
        public event EventHandler<DiscordConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public bool IsConnected { get; private set; } = true;
        public List<RpcProfile> SetCalls { get; } = [];
        public int DisconnectCalls { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReinitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPresenceAsync(RpcProfile profile, CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                SetCalls.Add(profile);
            }

            return Task.CompletedTask;
        }

        public Task ClearPresenceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCalls++;
            IsConnected = false;
            ConnectionStatusChanged?.Invoke(this, new DiscordConnectionStatusChangedEventArgs(false));
            return Task.CompletedTask;
        }
    }
}