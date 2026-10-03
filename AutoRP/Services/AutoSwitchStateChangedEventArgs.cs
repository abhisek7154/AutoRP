using AutoRP.Models;

namespace AutoRP.Services;

public sealed class AutoSwitchStateChangedEventArgs : EventArgs
{
    public AutoSwitchStateChangedEventArgs(
        ActiveApplication? currentApplication,
        RpcProfile? matchedProfile,
        DateTimeOffset? lastSwitchTime,
        string statusMessage)
    {
        CurrentApplication = currentApplication;
        MatchedProfile = matchedProfile;
        LastSwitchTime = lastSwitchTime;
        StatusMessage = statusMessage;
    }

    public ActiveApplication? CurrentApplication { get; }
    public RpcProfile? MatchedProfile { get; }
    public DateTimeOffset? LastSwitchTime { get; }
    public string StatusMessage { get; }
}