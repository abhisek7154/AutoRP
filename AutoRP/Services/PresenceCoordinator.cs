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

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);
        Volatile.Write(ref stopStarted, 0);
        await discordRpcService.ConnectAsync(cancellationToken);
        autoSwitchService.Start();
        logger.LogInformation("AutoRP monitoring is ready with a {PollingInterval} polling interval.", options.PollingInterval);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref stopStarted, 1) != 0)
        {
            return;
        }

        autoSwitchService.Stop();
        if (discordRpcService.IsConnected)
        {
            await discordRpcService.ClearPresenceAsync(cancellationToken);
        }

        await discordRpcService.DisconnectAsync(cancellationToken);
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
            });
    }

    private void OnAutomaticSwitchingStateChanged(object? sender, AutoSwitchStateChangedEventArgs e)
    {
        AutomaticSwitchingStateChanged?.Invoke(this, e);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _ = ReinitializeDiscordAsync();
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
