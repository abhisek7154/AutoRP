using AutoRP.Models;
using AutoRP.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AutoRP.Tests;

public sealed class AdvancedMatchingTests
{
    [Fact]
    public void MatchesMultipleProcessesCaseInsensitively()
    {
        var manager = CreateManager(new RpcProfile("", "Multiple", "Testing")
        {
            Name = "Multiple",
            ProcessNames = ["Code.exe", "firefox.exe"]
        });

        Assert.Equal("Multiple", manager.FindProfile(Application("FIREFOX.EXE"))?.Name);
        Assert.Equal("Multiple", manager.FindProfile(Application("code.exe"))?.Name);
    }

    [Fact]
    public void RequiresProcessAndWindowTitleCriteriaTogether()
    {
        var manager = CreateManager(new RpcProfile("", "ShriERP", "Testing")
        {
            Name = "ShriERP",
            ProcessNames = ["Code.exe"],
            WindowTitleContains = ["ShriERP"]
        });

        Assert.Null(manager.FindProfile(Application("Code.exe", "AutoRP.sln")));
        Assert.Equal("ShriERP", manager.FindProfile(Application("Code.exe", "ShriERP.sln - Visual Studio Code"))?.Name);
        Assert.Null(manager.FindProfile(Application("firefox.exe", "ShriERP.sln")));
    }

    [Fact]
    public void ChoosesHighestPriorityAndKeepsConfigurationOrderForTies()
    {
        var first = new RpcProfile("Code.exe", "First", "Testing") { Name = "First", Priority = 20 };
        var tied = new RpcProfile("Code.exe", "Tied", "Testing") { Name = "Tied", Priority = 20 };
        var lower = new RpcProfile("Code.exe", "Lower", "Testing") { Name = "Lower", Priority = 10 };
        var manager = CreateManager(first, tied, lower);

        Assert.Equal("First", manager.FindProfile(Application("Code.exe"))?.Name);

        manager = CreateManager(lower, tied);
        Assert.Equal("Tied", manager.FindProfile(Application("Code.exe"))?.Name);
    }

    [Fact]
    public void DisabledAndEmptyProfilesNeverMatch()
    {
        var disabled = new RpcProfile("Code.exe", "Disabled", "Testing") { Name = "Disabled", IsEnabled = false };
        var empty = new RpcProfile("", "Empty", "Testing") { Name = "Empty" };
        var manager = CreateManager(disabled, empty);

        Assert.Null(manager.FindProfile(Application("Code.exe")));
        Assert.Null(manager.FindProfile(Application("unknown.exe", "Empty")));
    }

    [Fact]
    public void LegacySingularProcessNameStillMatches()
    {
        var manager = CreateManager(new RpcProfile("legacy.exe", "Legacy", "Testing") { Name = "Legacy" });

        Assert.Equal("Legacy", manager.FindProfile(Application("LEGACY.EXE"))?.Name);
    }

    [Fact]
    public async Task SameProcessDifferentTitlesSwitchProfiles()
    {
        var general = new RpcProfile("Code.exe", "General Coding", "Testing") { Name = "General", Priority = 1 };
        var specialized = new RpcProfile("Code.exe", "ShriERP Coding", "Testing")
        {
            Name = "ShriERP",
            Priority = 10,
            WindowTitleContains = ["ShriERP"]
        };
        var manager = CreateManager(general, specialized);
        var activeWindow = new FakeActiveWindowService(Application("Code.exe", "ShriERP.sln - Code"));
        var discord = new FakeDiscordRpcService();
        using var switchService = new AutoSwitchService(
            activeWindow,
            manager,
            discord,
            LoggerFactory.Create(builder => { }).CreateLogger<AutoSwitchService>());

        switchService.Start();
        await WaitForAsync(() => discord.SetCalls.Count == 1);
        Assert.Equal("ShriERP", discord.SetCalls[0].Name);

        activeWindow.Raise(Application("Code.exe", "AutoRP.sln - Code"));
        await WaitForAsync(() => discord.SetCalls.Count == 2);
        Assert.Equal("General", discord.SetCalls[1].Name);
    }

    [Fact]
    public async Task RapidTitleTransitionsLeaveLatestProfileApplied()
    {
        var first = new RpcProfile("Code.exe", "First", "Testing")
        {
            Name = "First",
            WindowTitleContains = ["One"],
            Priority = 1
        };
        var second = new RpcProfile("Code.exe", "Second", "Testing")
        {
            Name = "Second",
            WindowTitleContains = ["Two"],
            Priority = 1
        };
        var manager = CreateManager(first, second);
        var activeWindow = new FakeActiveWindowService(Application("Code.exe", "One - Code"));
        var discord = new FakeDiscordRpcService { BlockFirstCall = true };
        using var switchService = new AutoSwitchService(
            activeWindow,
            manager,
            discord,
            LoggerFactory.Create(builder => { }).CreateLogger<AutoSwitchService>());

        switchService.Start();
        await discord.FirstCallStarted.Task;
        activeWindow.Raise(Application("Code.exe", "Two - Code"));
        discord.ReleaseFirstCall.TrySetResult();

        await WaitForAsync(() => discord.SetCalls.Count >= 2);
        Assert.Equal("Second", discord.SetCalls[^1].Name);
    }

    private static PresenceProfileManager CreateManager(params RpcProfile[] profiles)
    {
        return new PresenceProfileManager(new InMemoryProfileConfigurationService { Profiles = profiles });
    }

    private static ActiveApplication Application(string executableName, string? title = null)
    {
        return new ActiveApplication(
            42,
            Path.GetFileNameWithoutExtension(executableName),
            executableName,
            title ?? executableName);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class InMemoryProfileConfigurationService : IProfileConfigurationService
    {
        public event EventHandler? ProfilesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<RpcProfile> Profiles { get; init; } = [];
        public RpcProfile AddProfile(RpcProfile profile) => profile;
        public void UpdateProfile(string originalName, RpcProfile profile) { }
        public bool DeleteProfile(string profileName) => false;
        public void SetProfileEnabled(string profileName, bool enabled) { }
    }

    private sealed class FakeActiveWindowService(ActiveApplication? current) : IActiveWindowService
    {
        public event EventHandler<ActiveApplicationChangedEventArgs>? ApplicationChanged;
        public ActiveApplication? CurrentApplication { get; private set; } = current;
        public ActiveApplication? GetActiveApplication() => CurrentApplication;
        public void Start() { }
        public void Stop() { }

        public void Raise(ActiveApplication application)
        {
            var previous = CurrentApplication;
            CurrentApplication = application;
            ApplicationChanged?.Invoke(this, new ActiveApplicationChangedEventArgs(previous, application));
        }
    }

    private sealed class FakeDiscordRpcService : IDiscordRpcService
    {
        public event EventHandler<DiscordConnectionStatusChangedEventArgs>? ConnectionStatusChanged
        {
            add { }
            remove { }
        }
        public bool IsConnected => true;
        public List<RpcProfile> SetCalls { get; } = [];
        public bool BlockFirstCall { get; init; }
        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetPresenceAsync(RpcProfile profile, CancellationToken cancellationToken = default)
        {
            SetCalls.Add(profile);
            return WaitForFirstCallAsync();
        }

        private async Task WaitForFirstCallAsync()
        {
            if (BlockFirstCall && SetCalls.Count == 1)
            {
                FirstCallStarted.TrySetResult();
                await ReleaseFirstCall.Task;
            }
        }
        public Task ClearPresenceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
