namespace AutoRP.Services;

public sealed class DiscordConnectionStatusChangedEventArgs(bool isConnected) : EventArgs
{
    public bool IsConnected { get; } = isConnected;
}