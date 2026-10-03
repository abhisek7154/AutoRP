using AutoRP.Configuration;
using AutoRP.Models;
using AutoRP.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AutoRP.Tests;

public sealed class ActivityNameTests
{
    [Theory]
    [InlineData("firefox.exe", "Mozilla Firefox - Home", "Firefox", "firefox")]
    [InlineData("chrome.exe", "Google Chrome - Home", "Google Chrome", "chrome")]
    [InlineData("Code.exe", "AutoRP.sln - Visual Studio Code", "Visual Studio Code", "vscode")]
    [InlineData("Spotify.exe", "Spotify", "Spotify", "spotify")]
    [InlineData("firefox.exe", "Stranger Things - Netflix", "Netflix", "netflix")]
    [InlineData("chrome.exe", "Jujutsu Kaisen - Crunchyroll", "Crunchyroll", "crunchyroll")]
    [InlineData("firefox.exe", "Song name • YouTube Music", "YouTube Music", "youtube_music")]
    public void OutgoingActivityPayloadContainsResolvedNameAndArtwork(
        string processName,
        string windowTitle,
        string expectedName,
        string expectedArtwork)
    {
        using var manager = CreateDefaultManager();
        var application = Application(processName, windowTitle);
        var matchedProfile = Assert.IsType<RpcProfile>(manager.FindProfile(application));
        var outgoingProfile = matchedProfile with
        {
            ActivityName = ActivityNameResolver.Resolve(application, matchedProfile)
        };

        var payload = SerializeOutgoingActivity(outgoingProfile);

        Assert.Equal(expectedName, payload.Value<string>("name"));
        Assert.Equal(matchedProfile.Details, payload.Value<string>("details"));
        Assert.Equal(matchedProfile.State, payload.Value<string>("state"));
        Assert.Equal(expectedArtwork, payload["assets"]?.Value<string>("large_image"));
    }

    [Theory]
    [InlineData("firefox.exe", "netflix | Continue Watching", "Netflix", "netflix")]
    [InlineData("firefox.exe", "CRUNCHYROLL - WATCH ANIME", "Crunchyroll", "crunchyroll")]
    [InlineData("firefox.exe", "YouTube Music - Home", "YouTube Music", "youtube_music")]
    [InlineData("chrome.exe", "Netflix | Continue Watching", "Netflix", "netflix")]
    [InlineData("chrome.exe", "Crunchyroll - Watch Anime", "Crunchyroll", "crunchyroll")]
    [InlineData("chrome.exe", "YouTube Music - Home", "YouTube Music", "youtube_music")]
    public void BrowserTitlesSelectServiceProfileAndOutgoingName(
        string browserProcess,
        string title,
        string expectedName,
        string expectedArtwork)
    {
        using var manager = CreateDefaultManager();
        var application = Application(browserProcess, title);
        var profile = Assert.IsType<RpcProfile>(manager.FindProfile(application));

        Assert.Equal(expectedName, profile.ActivityName);
        Assert.Contains(browserProcess, profile.ProcessNames!, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(title.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase)
            ? "YouTube Music"
            : expectedName, profile.WindowTitleContains!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expectedArtwork, profile.LargeImageKey);
        Assert.DoesNotContain(profile.ProcessNames!, name => name.Equals("netflix.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("crunchyroll.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("youtube-music.exe", StringComparison.OrdinalIgnoreCase));

        var payload = SerializeOutgoingActivity(
            profile with { ActivityName = ActivityNameResolver.Resolve(application, profile) });
        Assert.Equal(expectedName, payload.Value<string>("name"));
        Assert.Equal(expectedArtwork, payload["assets"]?.Value<string>("large_image"));
    }

    [Fact]
    public async Task SwitchingBetweenProfilesChangesNameAndArtworkAndUnmatchedAppKeepsPresence()
    {
        using var manager = CreateDefaultManager();
        var activeWindow = new FakeActiveWindowService(Application("firefox.exe", "Firefox - Home"));
        var discord = new FakeDiscordRpcService();
        using var switcher = CreateSwitchService(activeWindow, manager, discord);

        switcher.Start();
        await WaitForAsync(() => discord.SetCalls.Count == 1);
        activeWindow.Raise(Application("firefox.exe", "Show - Netflix"));
        await WaitForAsync(() => discord.SetCalls.Count == 2);
        activeWindow.Raise(Application("chrome.exe", "Anime - Crunchyroll"));
        await WaitForAsync(() => discord.SetCalls.Count == 3);
        activeWindow.Raise(Application("chrome.exe", "Song • YouTube Music"));
        await WaitForAsync(() => discord.SetCalls.Count == 4);

        var expected = new[]
        {
            ("Firefox", "firefox"),
            ("Netflix", "netflix"),
            ("Crunchyroll", "crunchyroll"),
            ("YouTube Music", "youtube_music")
        };
        for (var index = 0; index < expected.Length; index++)
        {
            var payload = SerializeOutgoingActivity(discord.SetCalls[index]);
            Assert.Equal(expected[index].Item1, payload.Value<string>("name"));
            Assert.Equal(expected[index].Item2, payload["assets"]?.Value<string>("large_image"));
        }

        activeWindow.Raise(Application("unknown.exe", "Unmatched window"));
        await Task.Delay(30);
        Assert.Equal(4, discord.SetCalls.Count);
        Assert.Equal("YouTube Music", switcher.EffectiveProfile?.ActivityName);
        Assert.Equal("youtube_music", switcher.EffectiveProfile?.LargeImageKey);
    }

    [Fact]
    public async Task CustomProfileUsesTheDetectedProcessNameAndStillPublishesItsArtwork()
    {
        using var manager = CreateManager(new RpcProfile("obs64.exe", "Streaming", "Live", LargeImageKey: "my_custom_art")
        {
            Name = "My Streaming Profile"
        });
        var activeWindow = new FakeActiveWindowService(Application("obs64.exe", "OBS"));
        var discord = new FakeDiscordRpcService();
        using var switcher = CreateSwitchService(activeWindow, manager, discord);

        switcher.Start();
        await WaitForAsync(() => discord.SetCalls.Count == 1);

        var payload = SerializeOutgoingActivity(discord.SetCalls[0]);
        Assert.Equal("obs64", payload.Value<string>("name"));
        Assert.Equal("my_custom_art", payload["assets"]?.Value<string>("large_image"));
    }

    [Fact]
    public async Task ReconnectReappliesTheResolvedNameAndArtwork()
    {
        using var manager = CreateDefaultManager();
        var activeWindow = new FakeActiveWindowService(Application("firefox.exe", "Watch - Netflix"));
        var discord = new FakeDiscordRpcService { IsConnected = false };
        using var switcher = CreateSwitchService(activeWindow, manager, discord);

        switcher.Start();
        await Task.Yield();
        Assert.Empty(discord.SetCalls);

        discord.SetConnected(true);
        await WaitForAsync(() => discord.SetCalls.Count == 1);

        var payload = SerializeOutgoingActivity(discord.SetCalls[0]);
        Assert.Equal("Netflix", payload.Value<string>("name"));
        Assert.Equal("netflix", payload["assets"]?.Value<string>("large_image"));
    }

    private static PresenceProfileManager CreateDefaultManager()
    {
        return CreateManager(new AutoRpOptions().Profiles.ToArray());
    }

    private static PresenceProfileManager CreateManager(params RpcProfile[] profiles)
    {
        return new PresenceProfileManager(new InMemoryProfileConfigurationService(profiles));
    }

    private static AutoSwitchService CreateSwitchService(
        FakeActiveWindowService activeWindow,
        PresenceProfileManager manager,
        FakeDiscordRpcService discord)
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        return new AutoSwitchService(activeWindow, manager, discord, loggerFactory.CreateLogger<AutoSwitchService>());
    }

    private static ActiveApplication Application(string executableName, string title)
    {
        return new ActiveApplication(
            42,
            Path.GetFileNameWithoutExtension(executableName),
            executableName,
            title);
    }

    private static JObject SerializeOutgoingActivity(RpcProfile profile)
    {
        // DiscordRpcClient.SetPresence clones this object before placing it in the SET_ACTIVITY payload.
        return JObject.Parse(JsonConvert.SerializeObject(DiscordRpcService.CreatePresence(profile).Clone()));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class InMemoryProfileConfigurationService(IReadOnlyList<RpcProfile> profiles) : IProfileConfigurationService
    {
        public event EventHandler? ProfilesChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<RpcProfile> Profiles { get; } = profiles;
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
        public event EventHandler<DiscordConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
        public bool IsConnected { get; set; } = true;
        public List<RpcProfile> SetCalls { get; } = [];
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
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SetConnected(bool connected)
        {
            IsConnected = connected;
            ConnectionStatusChanged?.Invoke(this, new DiscordConnectionStatusChangedEventArgs(connected));
        }
    }
}
