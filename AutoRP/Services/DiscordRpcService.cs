using AutoRP.Configuration;
using AutoRP.Models;
using DiscordRPC;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace AutoRP.Services;

public sealed class DiscordRpcService(
    AutoRpOptions options,
    ISettingsService settingsService,
    ILogger<DiscordRpcService> logger) : IDiscordRpcService, IDisposable
{
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim connectionGate = new(1, 1);
    private readonly object stateLock = new();
    private DiscordRpcClient? client;
    private Timer? reconnectTimer;
    private RpcProfile? lastProfile;
    private bool isConnected;
    private bool isDisposed;
    private long reconnectGeneration;

    public event EventHandler<DiscordConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

    public bool IsConnected
    {
        get
        {
            lock (stateLock)
            {
                return isConnected;
            }
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            EnsureNotDisposed();
            TryConnect();
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public async Task ReinitializeAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(cancellationToken);
        await ConnectAsync(cancellationToken);
    }

    public async Task SetPresenceAsync(RpcProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await ConnectAsync(cancellationToken);

        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsConnected || client is null)
            {
                return;
            }

            if (ProfilesEqual(profile, lastProfile))
            {
                logger.LogDebug("Skipped duplicate Discord Rich Presence update.");
                return;
            }

            client.SetPresence(CreatePresence(profile));
            lastProfile = profile;
            logger.LogInformation("Discord Rich Presence updated.");
        }
        catch (Exception exception)
        {
            HandleConnectionFailure(exception);
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public async Task ClearPresenceAsync(CancellationToken cancellationToken = default)
    {
        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (client is not null && IsConnected)
            {
                client.ClearPresence();
            }

            lastProfile = null;
            logger.LogInformation("Discord Rich Presence cleared.");
        }
        catch (Exception exception)
        {
            HandleConnectionFailure(exception);
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref reconnectGeneration);
        await connectionGate.WaitAsync(cancellationToken);
        try
        {
            reconnectTimer?.Dispose();
            reconnectTimer = null;
            client?.Dispose();
            client = null;
            lastProfile = null;
            SetConnectionState(false);
            logger.LogInformation("Discord RPC service disconnected.");
        }
        finally
        {
            connectionGate.Release();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        connectionGate.Dispose();
    }

    private void TryConnect()
    {
        if (IsConnected)
        {
            return;
        }

        var applicationId = string.IsNullOrWhiteSpace(settingsService.Current.DiscordApplicationId)
            ? options.DiscordApplicationId
            : settingsService.Current.DiscordApplicationId;
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            logger.LogWarning("Discord RPC is disabled because DiscordApplicationId is not configured.");
            ScheduleReconnect();
            return;
        }

        try
        {
            client?.Dispose();
            client = new DiscordRpcClient(applicationId);
            client.Initialize();
            SetConnectionState(true);
            if (lastProfile is not null)
            {
                client.SetPresence(CreatePresence(lastProfile));
            }
            reconnectTimer?.Dispose();
            reconnectTimer = null;
            logger.LogInformation("Connected to Discord Rich Presence.");
        }
        catch (Exception exception)
        {
            HandleConnectionFailure(exception);
        }
    }

    private void ScheduleReconnect()
    {
        if (reconnectTimer is not null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref reconnectGeneration);
        reconnectTimer = new Timer(
            static state =>
            {
                var (service, timerGeneration) = ((DiscordRpcService Service, long Generation))state!;
                _ = service.ReconnectFromTimerAsync(timerGeneration);
            },
            (this, generation),
            ReconnectInterval,
            ReconnectInterval);
    }

    private async Task ReconnectFromTimerAsync(long generation)
    {
        if (isDisposed || generation != Volatile.Read(ref reconnectGeneration))
        {
            return;
        }

        try
        {
            await connectionGate.WaitAsync();
            try
            {
                if (isDisposed || generation != Volatile.Read(ref reconnectGeneration))
                {
                    return;
                }

                TryConnect();
            }
            finally
            {
                connectionGate.Release();
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Discord reconnect attempt failed.");
        }
    }

    private void HandleConnectionFailure(Exception exception)
    {
        SetConnectionState(false);
        client?.Dispose();
        client = null;
        ScheduleReconnect();
        logger.LogWarning(exception, "Discord is unavailable; Rich Presence will retry automatically.");
    }

    private void SetConnectionState(bool connected)
    {
        var changed = false;
        lock (stateLock)
        {
            changed = isConnected != connected;
            isConnected = connected;
        }

        if (changed)
        {
            try
            {
                ConnectionStatusChanged?.Invoke(this, new DiscordConnectionStatusChangedEventArgs(connected));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A Discord connection-status subscriber failed.");
            }
        }
    }

    private static RichPresence CreatePresence(RpcProfile profile)
    {
        return new RichPresence
        {
            Details = profile.Details,
            State = profile.State,
            Assets = CreateAssets(profile),
            Buttons = profile.Buttons?.Select(button => new Button
            {
                Label = button.Label,
                Url = button.Url
            }).ToArray()
        };
    }

    internal static Assets CreateAssets(RpcProfile profile)
    {
        return new Assets
        {
            LargeImageKey = profile.LargeImageKey,
            LargeImageText = profile.LargeImageText,
            SmallImageKey = profile.SmallImageKey,
            SmallImageText = profile.SmallImageText
        };
    }

    private static bool ProfilesEqual(RpcProfile? left, RpcProfile? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.ProcessName == right.ProcessName
            && left.Details == right.Details
            && left.State == right.State
            && left.LargeImageKey == right.LargeImageKey
            && left.LargeImageText == right.LargeImageText
            && left.SmallImageKey == right.SmallImageKey
            && left.SmallImageText == right.SmallImageText
            && (left.Buttons ?? Array.Empty<RpcButton>()).SequenceEqual(right.Buttons ?? Array.Empty<RpcButton>());
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
