using AutoRP.Configuration;
using AutoRP.Models;
using Microsoft.Extensions.Logging;

namespace AutoRP.Services;

public sealed class PresenceCoordinator
{
    private readonly IDiscordRpcService discordRpcService;
    private readonly AutoSwitchService autoSwitchService;
    private readonly AutoRpOptions options;
    private readonly ILogger<PresenceCoordinator> logger;

    public event EventHandler<AutoSwitchStateChangedEventArgs>? AutomaticSwitchingStateChanged;

    public PresenceCoordinator(
        IDiscordRpcService discordRpcService,
        AutoSwitchService autoSwitchService,
        AutoRpOptions options,
        ILogger<PresenceCoordinator> logger)
    {
        this.discordRpcService = discordRpcService;
        this.autoSwitchService = autoSwitchService;
        this.options = options;
        this.logger = logger;
        autoSwitchService.StateChanged += OnAutomaticSwitchingStateChanged;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await discordRpcService.ConnectAsync(cancellationToken);
        autoSwitchService.Start();
        logger.LogInformation("AutoRP monitoring is ready with a {PollingInterval} polling interval.", options.PollingInterval);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        autoSwitchService.Stop();
        await discordRpcService.ClearPresenceAsync(cancellationToken);
        await discordRpcService.DisconnectAsync(cancellationToken);
    }

    public bool IsDiscordConnected => discordRpcService.IsConnected;
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
        return discordRpcService.SetPresenceAsync(new RpcProfile(
            ProcessName: "AutoRP",
            Details: "Building AutoRP",
            State: "Testing Discord Rich Presence",
            LargeImageKey: "autorp_anime",
            LargeImageText: "AutoRP",
            Buttons: new[]
            {
                new RpcButton("AutoRP project", "https://github.com/")
            }), cancellationToken);
    }

    public Task ClearPresenceAsync(CancellationToken cancellationToken = default)
    {
        return discordRpcService.ClearPresenceAsync(cancellationToken);
    }

    public string GetCurrentApplicationLabel()
    {
        var application = autoSwitchService.CurrentApplication;
        return application is null
            ? "No active application detected"
            : $"{application.ProcessName} - {application.WindowTitle}";
    }

    private void OnAutomaticSwitchingStateChanged(object? sender, AutoSwitchStateChangedEventArgs e)
    {
        AutomaticSwitchingStateChanged?.Invoke(this, e);
    }
}
