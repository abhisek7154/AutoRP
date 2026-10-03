using AutoRP.Models;

namespace AutoRP.Services;

public interface IPresenceProfileManager
{
    event EventHandler? ProfilesChanged;
    RpcProfile? FindProfile(ActiveApplication? application);
    IReadOnlyList<RpcProfile> Profiles { get; }
}