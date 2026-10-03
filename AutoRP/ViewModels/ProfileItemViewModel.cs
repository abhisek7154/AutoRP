using AutoRP.Models;

namespace AutoRP.ViewModels;

public sealed class ProfileItemViewModel
{
    public ProfileItemViewModel(RpcProfile profile)
    {
        Profile = profile;
    }

    public RpcProfile Profile { get; }
    public string Name => Profile.Name;
    public string ProcessName => Profile.ProcessName;
    public string ProcessNames => string.Join(", ", Profile.ProcessNames ?? [Profile.ProcessName]);
    public string WindowTitleContains => string.Join(", ", Profile.WindowTitleContains ?? []);
    public int Priority => Profile.Priority;
    public string Details => Profile.Details;
    public string State => Profile.State;
    public string Status => Profile.IsEnabled ? "ON" : "OFF";
}
