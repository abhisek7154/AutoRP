using AutoRP.Models;

namespace AutoRP.Services;

public interface IProfileConfigurationService
{
    event EventHandler? ProfilesChanged;
    IReadOnlyList<RpcProfile> Profiles { get; }
    RpcProfile AddProfile(RpcProfile profile);
    void UpdateProfile(string originalName, RpcProfile profile);
    bool DeleteProfile(string profileName);
    void SetProfileEnabled(string profileName, bool enabled);
}