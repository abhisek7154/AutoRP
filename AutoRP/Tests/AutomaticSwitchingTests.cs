using AutoRP.Configuration;
using AutoRP.Models;
using AutoRP.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AutoRP.Tests;

public sealed class AutomaticSwitchingTests
{
    [Fact]
    public void FindsProfileCaseInsensitivelyByExecutableName()
    {
        var profile = CreateManager().FindProfile(Application("CHROME.EXE"));

        Assert.NotNull(profile);
        Assert.Equal("Chrome", profile.Details);
    }

    [Fact]
    public void FirefoxAndVisualStudioCodeProfilesHaveDistinctArtworkKeys()
    {
        var manager = CreateManager();

        Assert.Equal("firefox", manager.FindProfile(Application("firefox.exe"))?.LargeImageKey);
        Assert.Equal("vscode", manager.FindProfile(Application("Code.exe"))?.LargeImageKey);
    }

    [Fact]
    public async Task ChangingSettingsReinitializesDiscordRpc()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"autor p-runtime-settings-{Guid.NewGuid():N}.json");
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var settings = new JsonSettingsService(loggerFactory.CreateLogger<JsonSettingsService>(), settingsPath);
        var activeWindow = new FakeActiveWindowService(Application("chrome.exe"));
        var discord = new FakeDiscordRpcService();
        var autoSwitch = CreateSwitchService(activeWindow, discord);
        _ = new PresenceCoordinator(
            discord,
            autoSwitch,
            settings,
            new AutoRpOptions { DiscordApplicationId = string.Empty },
            loggerFactory.CreateLogger<PresenceCoordinator>());

        settings.Update(new AutoRpSettings(DiscordApplicationId: "new-id"));
        await WaitForAsync(() => discord.ReinitializeCalls == 1);

        autoSwitch.Dispose();
        if (File.Exists(settingsPath))
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void ReturnsNullWhenNoProfileMatches()
    {
        Assert.Null(CreateManager().FindProfile(Application("unknown.exe")));
        Assert.Null(CreateManager().FindProfile(null));
    }

    [Fact]
    public async Task SuppressesDuplicateProfilesAndSwitchesBetweenProfiles()
    {
        var activeWindow = new FakeActiveWindowService(Application("chrome.exe"));
        var discord = new FakeDiscordRpcService();
        using var service = CreateSwitchService(activeWindow, discord);

        service.Start();
        await WaitForAsync(() => discord.SetCalls.Count == 1);
        activeWindow.Raise(Application("CHROME.EXE"));
        await Task.Yield();
        Assert.Single(discord.SetCalls);

        activeWindow.Raise(Application("Code.exe"));
        await WaitForAsync(() => discord.SetCalls.Count == 2);
        Assert.Equal("Code", discord.SetCalls[1].Details);
        Assert.Equal("vscode", discord.SetCalls[1].LargeImageKey);
    }

    [Fact]
    public async Task ClearsPresenceForUnmatchedApplication()
    {
        var activeWindow = new FakeActiveWindowService(Application("chrome.exe"));
        var discord = new FakeDiscordRpcService();
        using var service = CreateSwitchService(activeWindow, discord);

        service.Start();
        await WaitForAsync(() => discord.SetCalls.Count == 1);
        activeWindow.Raise(Application("unknown.exe"));

        await WaitForAsync(() => discord.ClearCalls == 1);
        Assert.Null(service.MatchedProfile);
    }

    [Fact]
    public async Task DoesNotSwitchWhenDisabled()
    {
        var activeWindow = new FakeActiveWindowService(Application("chrome.exe"));
        var discord = new FakeDiscordRpcService();
        using var service = CreateSwitchService(activeWindow, discord);
        service.SetEnabled(false);

        service.Start();
        await Task.Yield();

        Assert.Empty(discord.SetCalls);
        Assert.Equal("Chrome", service.MatchedProfile?.Details);
    }

    [Fact]
    public async Task AppliesCurrentProfileAfterDiscordReconnects()
    {
        var activeWindow = new FakeActiveWindowService(Application("chrome.exe"));
        var discord = new FakeDiscordRpcService { IsConnected = false };
        using var service = CreateSwitchService(activeWindow, discord);

        service.Start();
        await Task.Yield();
        Assert.Empty(discord.SetCalls);

        discord.SetConnected(true);
        await WaitForAsync(() => discord.SetCalls.Count == 1);
        Assert.Equal("Chrome", discord.SetCalls[0].Details);
    }

    [Fact]
    public async Task PausingAndResumingAutomaticSwitchingControlsApplications()
    {
        var activeWindow = new FakeActiveWindowService(Application("chrome.exe"));
        var discord = new FakeDiscordRpcService();
        using var service = CreateSwitchService(activeWindow, discord);

        service.Start();
        await WaitForAsync(() => discord.SetCalls.Count == 1);
        service.SetEnabled(false);
        activeWindow.Raise(Application("Code.exe"));
        await Task.Delay(20);
        Assert.Single(discord.SetCalls);

        service.SetEnabled(true);
        await WaitForAsync(() => discord.SetCalls.Count == 2);
        Assert.Equal("Code", discord.SetCalls[1].Details);
    }

    [Fact]
    public void DisposingAutomaticSwitchingStopsForegroundMonitoring()
    {
        var activeWindow = new FakeActiveWindowService(Application("chrome.exe"));
        var discord = new FakeDiscordRpcService();
        var service = CreateSwitchService(activeWindow, discord);

        service.Start();
        service.Dispose();

        Assert.Equal(1, activeWindow.StopCalls);
    }

    [Fact]
    public async Task StoppingAutomaticSwitchingDoesNotCommitQueuedPresence()
    {
        var activeWindow = new FakeActiveWindowService(Application("chrome.exe"));
        var discord = new FakeDiscordRpcService { BlockNextSetCall = true };
        using var service = CreateSwitchService(activeWindow, discord);

        service.Start();
        await discord.SetStarted.Task;
        service.Stop();
        discord.ReleaseSet.TrySetResult();

        await Task.Delay(20);
        Assert.Single(discord.SetCalls);
        Assert.Null(service.LastSwitchTime);
    }

    private static PresenceProfileManager CreateManager()
    {
        return new PresenceProfileManager(new InMemoryProfileConfigurationService
        {
            Profiles =
            [
                new RpcProfile("chrome.exe", "Chrome", "Browsing", LargeImageKey: "chrome"),
                new RpcProfile("Code.exe", "Code", "Coding", LargeImageKey: "vscode"),
                new RpcProfile("firefox.exe", "Firefox", "Browsing", LargeImageKey: "firefox")
            ]
        });
    }

    private static AutoSwitchService CreateSwitchService(FakeActiveWindowService activeWindow, FakeDiscordRpcService discord)
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var manager = CreateManager();
        return new AutoSwitchService(activeWindow, manager, discord, loggerFactory.CreateLogger<AutoSwitchService>());
    }

    private static ActiveApplication Application(string executableName)
    {
        return new ActiveApplication(42, Path.GetFileNameWithoutExtension(executableName), executableName, executableName);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class FakeActiveWindowService(ActiveApplication? current) : IActiveWindowService
    {
        public event EventHandler<ActiveApplicationChangedEventArgs>? ApplicationChanged;
        public ActiveApplication? CurrentApplication { get; private set; } = current;
        public int StopCalls { get; private set; }

        public ActiveApplication? GetActiveApplication() => CurrentApplication;
        public void Start() { }
        public void Stop() => StopCalls++;

        public void Raise(ActiveApplication? application)
        {
            var previous = CurrentApplication;
            CurrentApplication = application;
            ApplicationChanged?.Invoke(this, new ActiveApplicationChangedEventArgs(previous, application));
        }
    }

    private sealed class InMemoryProfileConfigurationService : IProfileConfigurationService
    {
        public event EventHandler? ProfilesChanged
        {
            add { }
            remove { }
        }
        public IReadOnlyList<RpcProfile> Profiles { get; set; } = [];
        public RpcProfile AddProfile(RpcProfile profile) => profile;
        public void UpdateProfile(string originalName, RpcProfile profile) { }
        public bool DeleteProfile(string profileName) => false;
        public void SetProfileEnabled(string profileName, bool enabled) { }
    }

    private sealed class FakeDiscordRpcService : IDiscordRpcService
    {
        public event EventHandler<DiscordConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public bool IsConnected { get; set; } = true;
        public List<RpcProfile> SetCalls { get; } = [];
        public int ReinitializeCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public bool BlockNextSetCall { get; set; }
        public TaskCompletionSource SetStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSet { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReinitializeAsync(CancellationToken cancellationToken = default)
        {
            ReinitializeCalls++;
            return Task.CompletedTask;
        }

        public async Task SetPresenceAsync(RpcProfile profile, CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                if (BlockNextSetCall)
                {
                    BlockNextSetCall = false;
                    SetStarted.TrySetResult();
                    await ReleaseSet.Task;
                }

                SetCalls.Add(profile);
            }
        }

        public Task ClearPresenceAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                ClearCalls++;
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void SetConnected(bool connected)
        {
            IsConnected = connected;
            ConnectionStatusChanged?.Invoke(this, new DiscordConnectionStatusChangedEventArgs(connected));
        }
    }
}
