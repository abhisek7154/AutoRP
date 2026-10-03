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
    private readonly object taskLock = new();
    private readonly HashSet<Task> backgroundTasks = [];
    private RpcProfile? effectiveProfile;
    private bool hasAppliedProfile;
    private long switchVersion;
    private bool isDisposed;
    private ActiveApplication? currentApplication;
    private RpcProfile? matchedProfile;
    private DateTimeOffset? lastSwitchTime;
    private string statusMessage = "Automatic switching is ready.";
    private bool isStarted;
    private bool isStopping;
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
    public RpcProfile? EffectiveProfile => effectiveProfile;
    public DateTimeOffset? LastSwitchTime => lastSwitchTime;
    public string StatusMessage => statusMessage;

    public void MarkPresenceCleared()
    {
        lock (stateLock)
        {
            effectiveProfile = null;
            hasAppliedProfile = false;
        }
    }

    public void Start()
    {
        lock (stateLock)
        {
            if (isStarted)
            {
                return;
            }

            isStarted = true;
            isStopping = false;
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
            isStopping = true;
            switchVersion++;
            hasAppliedProfile = false;
            effectiveProfile = null;
        }

        activeWindowService.ApplicationChanged -= OnApplicationChanged;
        activeWindowService.Stop();
    }

    public async Task StopAsync()
    {
        Stop();
        await activeWindowService.StopAsync();
        while (true)
        {
            Task[] pending;
            lock (taskLock)
            {
                pending = backgroundTasks.ToArray();
            }

            if (pending.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pending);
        }
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
            QueueApplyProfile(MatchedProfile, version, force: true);
        }
    }

    public void ProcessApplication(ActiveApplication? application)
    {
        var profile = profileManager.FindProfile(application);
        long version;
        lock (stateLock)
        {
            if (isStopping || isDisposed)
            {
                return;
            }

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
            SetStatus(hasAppliedProfile
                ? "No matching profile; keeping previous presence."
                : "No matching profile; waiting for a supported application.");
            return;
        }

        logger.LogInformation("Foreground application changed: {ProcessName}. Matched profile: {Details}.", application?.ExecutableName, profile.Details);
        SetStatus($"Matched profile: {profile.Details}");
        QueueApplyProfile(profile, version);
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
        StopAsync().GetAwaiter().GetResult();
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
            RpcProfile? profile;
            lock (stateLock)
            {
                version = switchVersion;
                profile = matchedProfile ?? effectiveProfile;
            }

            QueueApplyProfile(profile, version, force: true);
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
        if (profile is null)
        {
            return;
        }

        ActiveApplication? application;
        lock (stateLock)
        {
            if (isDisposed || version != switchVersion)
            {
                return;
            }

            application = currentApplication;
        }

        var outgoingProfile = profile with
        {
            ActivityName = ActivityNameResolver.Resolve(application, profile)
        };

        await applyGate.WaitAsync();
        try
        {
            lock (stateLock)
            {
                if (isDisposed || version != switchVersion || (!force && hasAppliedProfile && ProfilesEqual(outgoingProfile, effectiveProfile)))
                {
                    return;
                }
            }

            await discordRpcService.SetPresenceAsync(outgoingProfile);

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

            effectiveProfile = outgoingProfile;
            hasAppliedProfile = true;
            lastSwitchTime = DateTimeOffset.Now;
            SetStatus($"Discord presence updated: {profile.Details}");
            logger.LogInformation("Discord presence updated.");
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

    private void QueueApplyProfile(RpcProfile? profile, long version, bool force = false)
    {
        if (profile is null)
        {
            return;
        }

        Task task;
        lock (taskLock)
        {
            lock (stateLock)
            {
                if (isStopping || isDisposed || version != switchVersion)
                {
                    return;
                }
            }

            task = ApplyProfileAsync(profile, version, force);
            backgroundTasks.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                lock (taskLock)
                {
                    backgroundTasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
