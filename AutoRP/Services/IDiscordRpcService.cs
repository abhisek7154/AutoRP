using AutoRP.Models;

namespace AutoRP.Services;

public interface IDiscordRpcService
{
    event EventHandler<DiscordConnectionStatusChangedEventArgs>? ConnectionStatusChanged;
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task SetPresenceAsync(RpcProfile profile, CancellationToken cancellationToken = default);
    Task ClearPresenceAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
