using AutoRP.Configuration;
using AutoRP.Models;
using Microsoft.Extensions.Logging;

namespace AutoRP.Services;

public sealed class PresenceCoordinator : IDisposable
{
    private readonly IDiscordRpcService discordRpcService;
    private readonly AutoSwitchService autoSwitchService;
    private readonly ISettingsService settingsService;
    private readonly AutoRpOptions options;
    private readonly ILogger<PresenceCoordinator> logger;
    private int isDisposed;
    private int stopStarted;
    private readonly object lifecycleLock = new();
    private Task? startTask;
    private Task? stopTask;
    private readonly object backgroundLock = new();
    private readonly HashSet<Task> backgroundTasks = [];

    public event EventHandler<AutoSwitchStateChangedEventArgs>? AutomaticSwitchingStateChanged;

    public PresenceCoordinator(
        IDiscordRpcService discordRpcService,
        AutoSwitchService autoSwitchService,
        ISettingsService settingsService,
        AutoRpOptions options,
        ILogger<PresenceCoordinator> logger)
    {
        this.discordRpcService = discordRpcService;
        this.autoSwitchService = autoSwitchService;
        this.settingsService = settingsService;
        this.options = options;
        this.logger = logger;
        autoSwitchService.StateChanged += OnAutomaticSwitchingStateChanged;
        settingsService.SettingsChanged += OnSettingsChanged;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);
            if (stopTask is not null)
            {
                return Task.CompletedTask;
            }

            startTask ??= StartCoreAsync(cancellationToken);
            return startTask;
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await discordRpcService.ConnectAsync(cancellationToken);
        if (Volatile.Read(ref stopStarted) != 0)
        {
            return;
        }

        autoSwitchService.Start();
        logger.LogInformation("AutoRP monitoring is ready with a {PollingInterval} polling interval.", options.PollingInterval);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (lifecycleLock)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }

            Interlocked.Exchange(ref stopStarted, 1);
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            stopTask = completion.Task;
        }

        _ = StopCoreAsync(cancellationToken, completion);
        return completion.Task;
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken, TaskCompletionSource completion)
    {
        try
        {
            autoSwitchService.StateChanged -= OnAutomaticSwitchingStateChanged;
            settingsService.SettingsChanged -= OnSettingsChanged;
            Task? startup;
            lock (lifecycleLock)
            {
                startup = startTask;
            }

            if (startup is not null)
            {
                try
                {
                    await startup;
                }
                catch (OperationCanceledException)
                {
                    // Window closure can cancel an in-progress initial connection.
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "AutoRP startup ended while shutdown was beginning.");
                }
            }

            logger.LogInformation("Stopping automatic switching and foreground monitoring.");
            await autoSwitchService.StopAsync();
            while (true)
            {
                Task[] pending;
                lock (backgroundLock)
                {
                    pending = backgroundTasks.ToArray();
                }

                if (pending.Length == 0)
                {
                    break;
                }

                await Task.WhenAll(pending);
            }

            try
            {
                if (discordRpcService.IsConnected)
                {
                    logger.LogInformation("Disconnecting Discord Rich Presence.");
                    await discordRpcService.ClearPresenceAsync(cancellationToken);
                }
            }
            finally
            {
                logger.LogInformation("Disconnecting Discord RPC client.");
                await discordRpcService.DisconnectAsync(CancellationToken.None);
            }

            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        autoSwitchService.StateChanged -= OnAutomaticSwitchingStateChanged;
        settingsService.SettingsChanged -= OnSettingsChanged;
    }

    public bool IsDiscordConnected => discordRpcService.IsConnected;
    public bool IsDiscordApplicationIdConfigured => !string.IsNullOrWhiteSpace(settingsService.Current.DiscordApplicationId)
        || !string.IsNullOrWhiteSpace(options.DiscordApplicationId);
    public bool IsAutomaticSwitchingEnabled => autoSwitchService.IsEnabled;
    public ActiveApplication? CurrentApplication => autoSwitchService.CurrentApplication;
    public RpcProfile? MatchedProfile => autoSwitchService.MatchedProfile;
    public DateTimeOffset? LastSwitchTime => autoSwitchService.LastSwitchTime;
    public string AutomaticSwitchingStatus => autoSwitchService.StatusMessage;

    public void SetAutomaticSwitchingEnabled(bool enabled)
    {
        autoSwitchService.SetEnabled(enabled);
    }

    public Task ConnectDiscordAsync(CancellationToken cancellationToken = default)
    {
        return discordRpcService.ConnectAsync(cancellationToken);
    }

    public Task DisconnectDiscordAsync(CancellationToken cancellationToken = default)
    {
        return discordRpcService.DisconnectAsync(cancellationToken);
    }

    public Task SetSamplePresenceAsync(CancellationToken cancellationToken = default)
    {
        return discordRpcService.SetPresenceAsync(CreateSampleProfile(), cancellationToken);
    }

    public Task ClearPresenceAsync(CancellationToken cancellationToken = default)
    {
        autoSwitchService.MarkPresenceCleared();
        return discordRpcService.ClearPresenceAsync(cancellationToken);
    }

    public string GetCurrentApplicationLabel()
    {
        var application = autoSwitchService.CurrentApplication;
        return application is null
            ? "No active application detected"
            : $"{application.ProcessName} - {application.WindowTitle}";
    }

    internal static RpcProfile CreateSampleProfile()
    {
        return new RpcProfile(
            ProcessName: "AutoRP",
            Details: "Building AutoRP",
            State: "Testing Discord Rich Presence",
            LargeImageKey: "autorp_anime",
            LargeImageText: "AutoRP",
            Buttons: new[]
            {
                new RpcButton("AutoRP project", "https://github.com/")
            })
        {
            Name = "AutoRP",
            ActivityName = "AutoRP"
        };
    }

    private void OnAutomaticSwitchingStateChanged(object? sender, AutoSwitchStateChangedEventArgs e)
    {
        AutomaticSwitchingStateChanged?.Invoke(this, e);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Task task;
        lock (backgroundLock)
        {
            if (Volatile.Read(ref stopStarted) != 0 || Volatile.Read(ref isDisposed) != 0)
            {
                return;
            }

            task = ReinitializeDiscordAsync();
            backgroundTasks.Add(task);
        }

        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            lock (backgroundLock)
            {
                backgroundTasks.Remove(completed);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ReinitializeDiscordAsync()
    {
        try
        {
            await discordRpcService.ReinitializeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Discord RPC could not be reinitialized after settings changed.");
        }
    }
}
