using AutoRP.Models;

namespace AutoRP.Services;

public sealed class ActiveApplicationChangedEventArgs(
    ActiveApplication? previousApplication,
    ActiveApplication? currentApplication) : EventArgs
{
    public ActiveApplication? PreviousApplication { get; } = previousApplication;
    public ActiveApplication? CurrentApplication { get; } = currentApplication;
}
