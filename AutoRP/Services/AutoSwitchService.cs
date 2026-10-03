using AutoRP.Models;
using Microsoft.Extensions.Logging;

namespace AutoRP.Services;

public sealed class AutoSwitchService : IDisposable
{
    private readonly IActiveWindowService activeWindowService;
    private readonly IPresenceProfileManager profileManager;
    private readonly IDiscordRpcService discordRpcService;
    private readonly ILogger<AutoSwitchService> logger;
    private readonly object stateLock = new();
    private readonly SemaphoreSlim applyGate = new(1, 1);
    private RpcProfile? effectiveProfile;
    private bool hasAppliedProfile;
    private long switchVersion;
    private bool isDisposed;
    private ActiveApplication? currentApplication;
    private RpcProfile? matchedProfile;
    private DateTimeOffset? lastSwitchTime;
    private string statusMessage = "Automatic switching is ready.";
    private bool isStarted;
    private bool isEnabled = true;

    public AutoSwitchService(
        IActiveWindowService activeWindowService,
        IPresenceProfileManager profileManager,
        IDiscordRpcService discordRpcService,
        ILogger<AutoSwitchService> logger)
    {
        this.activeWindowService = activeWindowService;
        this.profileManager = profileManager;
        this.discordRpcService = discordRpcService;
        this.logger = logger;
        discordRpcService.ConnectionStatusChanged += OnConnectionStatusChanged;
        profileManager.ProfilesChanged += OnProfilesChanged;
    }

    public event EventHandler<AutoSwitchStateChangedEventArgs>? StateChanged;

    public bool IsEnabled
    {
        get
        {
            lock (stateLock)
            {
                return isEnabled;
            }
        }
    }

    public ActiveApplication? CurrentApplication => currentApplication;
    public RpcProfile? MatchedProfile => matchedProfile;
    public DateTimeOffset? LastSwitchTime => lastSwitchTime;
    public string StatusMessage => statusMessage;

    public void Start()
    {
        lock (stateLock)
        {
            if (isStarted)
            {
                return;
            }

            isStarted = true;
        }

        activeWindowService.ApplicationChanged += OnApplicationChanged;
        activeWindowService.Start();
        ProcessApplication(activeWindowService.CurrentApplication ?? activeWindowService.GetActiveApplication());
    }

    public void Stop()
    {
        lock (stateLock)
        {
            if (!isStarted)
            {
                return;
            }

            isStarted = false;
            switchVersion++;
            hasAppliedProfile = false;
            effectiveProfile = null;
        }

        activeWindowService.ApplicationChanged -= OnApplicationChanged;
        activeWindowService.Stop();
    }

    public void SetEnabled(bool enabled)
    {
        long version;
        lock (stateLock)
        {
            isEnabled = enabled;
            version = ++switchVersion;
            statusMessage = enabled
                ? "Automatic switching is enabled."
                : "Automatic switching is disabled.";
        }

        PublishState();
        logger.LogInformation("Automatic presence switching {State}.", enabled ? "enabled" : "disabled");

        if (enabled)
        {
            _ = ApplyProfileAsync(MatchedProfile, version, force: true);
        }
    }

    public void ProcessApplication(ActiveApplication? application)
    {
        var profile = profileManager.FindProfile(application);
        long version;
        lock (stateLock)
        {
            currentApplication = application;
            matchedProfile = profile;
            version = ++switchVersion;
        }

        if (!IsEnabled)
        {
            PublishState();
            return;
        }

        if (profile is null)
        {
            logger.LogInformation("Foreground application changed: {ProcessName}. No matching profile.", application?.ExecutableName ?? "unknown");
            SetStatus("No matching profile; Discord presence cleared.");
            _ = ApplyProfileAsync(null, version);
            return;
        }

        logger.LogInformation("Foreground application changed: {ProcessName}. Matched profile: {Details}.", application?.ExecutableName, profile.Details);
        SetStatus($"Matched profile: {profile.Details}");
        _ = ApplyProfileAsync(profile, version);
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            switchVersion++;
        }

        discordRpcService.ConnectionStatusChanged -= OnConnectionStatusChanged;
        profileManager.ProfilesChanged -= OnProfilesChanged;
        Stop();
        applyGate.Wait();
        applyGate.Release();
        applyGate.Dispose();
    }

    private void OnApplicationChanged(object? sender, ActiveApplicationChangedEventArgs e)
    {
        ProcessApplication(e.CurrentApplication);
    }

    private void OnConnectionStatusChanged(object? sender, DiscordConnectionStatusChangedEventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        if (!e.IsConnected)
        {
            hasAppliedProfile = false;
        }

        if (e.IsConnected && IsEnabled)
        {
            long version;
            lock (stateLock)
            {
                version = switchVersion;
            }

            _ = ApplyProfileAsync(matchedProfile, version);
        }

        PublishState();
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        ProcessApplication(CurrentApplication);
    }

    private async Task ApplyProfileAsync(RpcProfile? profile, long version, bool force = false)
    {
        lock (stateLock)
        {
            if (isDisposed || version != switchVersion || (!force && hasAppliedProfile && ProfilesEqual(profile, effectiveProfile)))
            {
                return;
            }
        }

        await applyGate.WaitAsync();
        try
        {
            lock (stateLock)
            {
                if (isDisposed || version != switchVersion || (!force && hasAppliedProfile && ProfilesEqual(profile, effectiveProfile)))
                {
                    return;
                }
            }

            if (profile is null)
            {
                await discordRpcService.ClearPresenceAsync();
            }
            else
            {
                await discordRpcService.SetPresenceAsync(profile);
            }

            lock (stateLock)
            {
                if (isDisposed || version != switchVersion)
                {
                    return;
                }
            }

            if (!discordRpcService.IsConnected)
            {
                SetStatus("Discord is unavailable; presence will apply after reconnect.");
                return;
            }

            effectiveProfile = profile;
            hasAppliedProfile = true;
            lastSwitchTime = DateTimeOffset.Now;
            SetStatus(profile is null ? "Discord presence cleared." : $"Discord presence updated: {profile.Details}");
            logger.LogInformation(profile is null ? "Discord presence cleared." : "Discord presence updated.");
        }
        catch (Exception exception)
        {
            SetStatus("Unable to apply Discord presence; will retry when Discord reconnects.");
            logger.LogWarning(exception, "Unable to apply automatic Discord presence.");
        }
        finally
        {
            applyGate.Release();
        }
    }

    private void SetStatus(string message)
    {
        lock (stateLock)
        {
            statusMessage = message;
        }

        PublishState();
    }

    private void PublishState()
    {
        StateChanged?.Invoke(this, new AutoSwitchStateChangedEventArgs(
            CurrentApplication,
            MatchedProfile,
            LastSwitchTime,
            StatusMessage));
    }

    private static bool ProfilesEqual(RpcProfile? left, RpcProfile? right)
    {
        return left is null && right is null
            || left is not null && right is not null && left == right;
    }
}