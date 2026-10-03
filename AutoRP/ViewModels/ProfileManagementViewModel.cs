using AutoRP.Models;
using AutoRP.Services;
using System.Collections.ObjectModel;

namespace AutoRP.ViewModels;

public sealed class ProfileManagementViewModel : IDisposable
{
    private readonly IProfileConfigurationService configurationService;
    private readonly IPresenceProfileManager profileManager;
    private readonly IDiscordRpcService discordRpcService;

    public ProfileManagementViewModel(
        IProfileConfigurationService configurationService,
        IPresenceProfileManager profileManager,
        IDiscordRpcService discordRpcService)
    {
        this.configurationService = configurationService;
        this.profileManager = profileManager;
        this.discordRpcService = discordRpcService;
        configurationService.ProfilesChanged += OnProfilesChanged;
        RefreshProfiles();
    }

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];
    public ProfileItemViewModel? SelectedProfile { get; set; }
    public string? ErrorMessage { get; private set; }
    public event EventHandler? ProfilesRefreshed;

    public void AddProfile(RpcProfile profile)
    {
        Execute(() => configurationService.AddProfile(profile));
    }

    public void UpdateProfile(string originalName, RpcProfile profile)
    {
        Execute(() => configurationService.UpdateProfile(originalName, profile));
    }

    public bool DeleteSelectedProfile()
    {
        if (SelectedProfile is null)
        {
            return false;
        }

        var profileName = SelectedProfile.Name;
        var deleted = false;
        Execute(() => deleted = configurationService.DeleteProfile(profileName));
        return deleted;
    }

    public void SetSelectedProfileEnabled(bool enabled)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        Execute(() => configurationService.SetProfileEnabled(SelectedProfile.Name, enabled));
    }

    public Task TestSelectedProfileAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile is null)
        {
            throw new InvalidOperationException("Select a profile first.");
        }

        return discordRpcService.SetPresenceAsync(SelectedProfile.Profile, cancellationToken);
    }

    public void RefreshProfiles()
    {
        var selectedName = SelectedProfile?.Name;
        Profiles.Clear();
        foreach (var profile in profileManager.Profiles)
        {
            Profiles.Add(new ProfileItemViewModel(profile));
        }

        SelectedProfile = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        ProfilesRefreshed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        configurationService.ProfilesChanged -= OnProfilesChanged;
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        RefreshProfiles();
    }

    private void Execute(Action action)
    {
        try
        {
            action();
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            throw;
        }
    }
}
