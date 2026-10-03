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

    [Fact]
    public void CloseToTrayOnlyHidesForAnOrdinaryClose()
    {
        Assert.True(UI.MainWindow.ShouldHideToTray(closeToTray: true, exitRequested: false));
        Assert.False(UI.MainWindow.ShouldHideToTray(closeToTray: false, exitRequested: false));
        Assert.False(UI.MainWindow.ShouldHideToTray(closeToTray: true, exitRequested: true));
    }

    [Fact]
    public async Task ShutdownWaitsForPresenceWorkAndPreventsFurtherMonitoringUpdates()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var activeWindow = new FakeActiveWindowService();
        var discord = new FakeDiscordRpcService { BlockPresenceUpdates = true };
        using var autoSwitch = new AutoSwitchService(
            activeWindow,
            new InMemoryProfileManager([new RpcProfile("firefox", "Browsing", "Firefox")]),
            discord,
            loggerFactory.CreateLogger<AutoSwitchService>());
        var path = Path.Combine(Path.GetTempPath(), $"autorp-shutdown-{Guid.NewGuid():N}.json");
        var settings = new JsonSettingsService(loggerFactory.CreateLogger<JsonSettingsService>(), path);
        var coordinator = new PresenceCoordinator(
            discord,
            autoSwitch,
            settings,
            new Configuration.AutoRpOptions { DiscordApplicationId = string.Empty },
            loggerFactory.CreateLogger<PresenceCoordinator>());

        await coordinator.StartAsync();
        activeWindow.Raise(new ActiveApplication(42, "firefox", "firefox.exe", "Firefox"));
        await discord.PresenceUpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopping = coordinator.StopAsync();
        Assert.False(stopping.IsCompleted);
        discord.ReleasePresenceUpdate.TrySetResult();
        await stopping;
        activeWindow.Raise(new ActiveApplication(43, "code", "code.exe", "VS Code"));

        Assert.Equal(1, activeWindow.StopCalls);
        Assert.Single(discord.SetCalls);
        Assert.Equal(1, discord.DisconnectCalls);
        coordinator.Dispose();
        if (File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public async Task ShutdownStillCompletesWhenDiscordIsAlreadyDisconnected()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var activeWindow = new FakeActiveWindowService();
        var discord = new FakeDiscordRpcService();
        using var autoSwitch = new AutoSwitchService(
            activeWindow,
            new InMemoryProfileManager(),
            discord,
            loggerFactory.CreateLogger<AutoSwitchService>());
        var settings = new JsonSettingsService(
            loggerFactory.CreateLogger<JsonSettingsService>(),
            Path.Combine(Path.GetTempPath(), $"autorp-disconnected-{Guid.NewGuid():N}.json"));
        var coordinator = new PresenceCoordinator(
            discord,
            autoSwitch,
            settings,
            new Configuration.AutoRpOptions { DiscordApplicationId = string.Empty },
            loggerFactory.CreateLogger<PresenceCoordinator>());

        await coordinator.StartAsync();
        discord.MarkDisconnected();
        await coordinator.StopAsync();

        Assert.Equal(1, activeWindow.StopCalls);
        Assert.Equal(1, discord.DisconnectCalls);
        coordinator.Dispose();
    }

    [Fact]
    public async Task ShutdownWaitsForStartupAndDoesNotRestartMonitoringAfterward()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var activeWindow = new FakeActiveWindowService();
        var discord = new FakeDiscordRpcService { BlockConnect = true };
        using var autoSwitch = new AutoSwitchService(
            activeWindow,
            new InMemoryProfileManager(),
            discord,
            loggerFactory.CreateLogger<AutoSwitchService>());
        var settings = new JsonSettingsService(
            loggerFactory.CreateLogger<JsonSettingsService>(),
            Path.Combine(Path.GetTempPath(), $"autorp-start-stop-{Guid.NewGuid():N}.json"));
        var coordinator = new PresenceCoordinator(
            discord,
            autoSwitch,
            settings,
            new Configuration.AutoRpOptions { DiscordApplicationId = string.Empty },
            loggerFactory.CreateLogger<PresenceCoordinator>());

        var starting = coordinator.StartAsync();
        await discord.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = coordinator.StopAsync();
        Assert.False(stopping.IsCompleted);
        discord.ReleaseConnect.TrySetResult();
        await Task.WhenAll(starting, stopping);

        Assert.Equal(0, activeWindow.StartCalls);
        Assert.Equal(0, activeWindow.StopCalls);
        Assert.Equal(1, discord.DisconnectCalls);
        coordinator.Dispose();
    }

    [Fact]
    public async Task ShutdownAwaitsAsynchronousForegroundMonitorStop()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var activeWindow = new FakeActiveWindowService { BlockStop = true };
        var discord = new FakeDiscordRpcService();
        using var autoSwitch = new AutoSwitchService(
            activeWindow,
            new InMemoryProfileManager(),
            discord,
            loggerFactory.CreateLogger<AutoSwitchService>());
        var settings = new JsonSettingsService(
            loggerFactory.CreateLogger<JsonSettingsService>(),
            Path.Combine(Path.GetTempPath(), $"autor p-async-monitor-stop-{Guid.NewGuid():N}.json"));
        var coordinator = new PresenceCoordinator(
            discord,
            autoSwitch,
            settings,
            new Configuration.AutoRpOptions { DiscordApplicationId = string.Empty },
            loggerFactory.CreateLogger<PresenceCoordinator>());
        await coordinator.StartAsync();

        var stopping = coordinator.StopAsync();
        await activeWindow.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopping.IsCompleted);
        activeWindow.ReleaseStop.TrySetResult();
        await stopping;

        Assert.Equal(1, activeWindow.StartCalls);
        Assert.Equal(1, activeWindow.StopCalls);
        Assert.Equal(1, discord.DisconnectCalls);
        coordinator.Dispose();
    }

    [Fact]
    public async Task DiscordAsyncDisposalStopsReconnectAndRejectsLaterWork()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var settingsPath = Path.Combine(Path.GetTempPath(), $"autorp-discord-dispose-{Guid.NewGuid():N}.json");
        var settings = new JsonSettingsService(loggerFactory.CreateLogger<JsonSettingsService>(), settingsPath);
        var service = new DiscordRpcService(
            new Configuration.AutoRpOptions { DiscordApplicationId = string.Empty },
            settings,
            loggerFactory.CreateLogger<DiscordRpcService>());

        await service.ConnectAsync();
        await service.DisposeAsync();
        await service.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ConnectAsync());
        if (File.Exists(settingsPath)) File.Delete(settingsPath);
    }

    [Fact]
    public void TrayDisposalIsSafeToRepeat()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var activeWindow = new FakeActiveWindowService();
        var discord = new FakeDiscordRpcService();
        using var autoSwitch = new AutoSwitchService(
            activeWindow,
            new InMemoryProfileManager(),
            discord,
            loggerFactory.CreateLogger<AutoSwitchService>());
        using var tray = new TrayService(autoSwitch, discord);

        tray.Dispose();
        tray.Dispose();

        Assert.False(tray.IsExitRequested);
    }

    [Fact]
    public async Task TrayExitInvokesTheIdempotentApplicationShutdownPath()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var discord = new FakeDiscordRpcService();
        using var autoSwitch = new AutoSwitchService(
            new FakeActiveWindowService(),
            new InMemoryProfileManager(),
            discord,
            loggerFactory.CreateLogger<AutoSwitchService>());
        using var tray = new TrayService(autoSwitch, discord);
        var exitRequests = 0;
        var serviceStopCalls = 0;
        var applicationShutdownCalls = 0;
        var shutdown = new ApplicationShutdownCoordinator(
            () =>
            {
                serviceStopCalls++;
                return Task.CompletedTask;
            },
            () => applicationShutdownCalls++);
        Task? shutdownTask = null;
        tray.ExitRequested += (_, _) =>
        {
            exitRequests++;
            shutdownTask = shutdown.ShutdownAsync();
        };

        tray.RequestExit();
        tray.RequestExit();
        Assert.NotNull(shutdownTask);
        await shutdownTask!;
        await shutdown.ShutdownAsync();

        Assert.True(tray.IsExitRequested);
        Assert.Equal(1, exitRequests);
        Assert.Equal(1, serviceStopCalls);
        Assert.Equal(1, applicationShutdownCalls);
    }

    [Fact]
    public async Task TrayExitStillShutsDownWhenDiscordCleanupThrows()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var activeWindow = new FakeActiveWindowService();
        var discord = new FakeDiscordRpcService { ThrowOnClear = true, ThrowOnDisconnect = true };
        using var autoSwitch = new AutoSwitchService(
            activeWindow,
            new InMemoryProfileManager(),
            discord,
            loggerFactory.CreateLogger<AutoSwitchService>());
        var settings = new JsonSettingsService(
            loggerFactory.CreateLogger<JsonSettingsService>(),
            Path.Combine(Path.GetTempPath(), $"autor p-shutdown-error-{Guid.NewGuid():N}.json"));
        var presence = new PresenceCoordinator(
            discord,
            autoSwitch,
            settings,
            new Configuration.AutoRpOptions { DiscordApplicationId = string.Empty },
            loggerFactory.CreateLogger<PresenceCoordinator>());
        await presence.StartAsync();
        using var tray = new TrayService(autoSwitch, discord);
        var applicationShutdownCalls = 0;
        var shutdown = new ApplicationShutdownCoordinator(
            () => presence.StopAsync(),
            () => applicationShutdownCalls++,
            loggerFactory.CreateLogger<ApplicationShutdownCoordinator>());
        Task? pendingShutdown = null;
        tray.ExitRequested += (_, _) => pendingShutdown = shutdown.ShutdownAsync();

        tray.RequestExit();
        await pendingShutdown!;

        Assert.Equal(1, activeWindow.StopCalls);
        Assert.Equal(1, discord.ClearCalls);
        Assert.Equal(1, discord.DisconnectCalls);
        Assert.Equal(1, applicationShutdownCalls);
        presence.Dispose();
    }

    private sealed class FakeActiveWindowService : IActiveWindowService
    {
        public event EventHandler<ActiveApplicationChangedEventArgs>? ApplicationChanged;
        public ActiveApplication? CurrentApplication => null;
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool BlockStop { get; init; }
        public TaskCompletionSource StopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ActiveApplication? GetActiveApplication() => null;
        public void Start() => StartCalls++;
        public void Stop() => StopCalls++;
        public async Task StopAsync()
        {
            if (BlockStop)
            {
                StopStarted.TrySetResult();
                await ReleaseStop.Task;
            }
        }

        public void Raise(ActiveApplication application)
        {
            ApplicationChanged?.Invoke(this, new ActiveApplicationChangedEventArgs(null, application));
        }
    }

    private sealed class InMemoryProfileManager(IReadOnlyList<RpcProfile>? profiles = null) : IPresenceProfileManager
    {
        public event EventHandler? ProfilesChanged
        {
            add { }
            remove { }
        }
        public IReadOnlyList<RpcProfile> Profiles => profiles ?? [];
        public RpcProfile? FindProfile(ActiveApplication? application) => application is null ? null : Profiles.FirstOrDefault();
    }

    private sealed class FakeDiscordRpcService : IDiscordRpcService
    {
        public event EventHandler<DiscordConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public bool IsConnected { get; private set; } = true;
        public List<RpcProfile> SetCalls { get; } = [];
        public int DisconnectCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public bool ThrowOnClear { get; init; }
        public bool ThrowOnDisconnect { get; init; }
        public bool BlockPresenceUpdates { get; init; }
        public TaskCompletionSource PresenceUpdateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePresenceUpdate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockConnect { get; init; }
        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseConnect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void MarkDisconnected() => IsConnected = false;

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (BlockConnect)
            {
                ConnectStarted.TrySetResult();
                await ReleaseConnect.Task.WaitAsync(cancellationToken);
            }
        }
        public Task ReinitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task SetPresenceAsync(RpcProfile profile, CancellationToken cancellationToken = default)
        {
            PresenceUpdateStarted.TrySetResult();
            if (BlockPresenceUpdates)
            {
                await ReleasePresenceUpdate.Task.WaitAsync(cancellationToken);
            }

            if (IsConnected)
            {
                SetCalls.Add(profile);
            }
        }

        public Task ClearPresenceAsync(CancellationToken cancellationToken = default)
        {
            ClearCalls++;
            return ThrowOnClear ? Task.FromException(new InvalidOperationException("Discord clear failed.")) : Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCalls++;
            if (ThrowOnDisconnect)
            {
                return Task.FromException(new InvalidOperationException("Discord disconnect failed."));
            }

            IsConnected = false;
            ConnectionStatusChanged?.Invoke(this, new DiscordConnectionStatusChangedEventArgs(false));
            return Task.CompletedTask;
        }
    }
}
