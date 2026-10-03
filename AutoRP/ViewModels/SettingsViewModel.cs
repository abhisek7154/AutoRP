using AutoRP.Models;
using AutoRP.Services;

namespace AutoRP.ViewModels;

public sealed class SettingsViewModel
{
    private readonly ISettingsService settingsService;
    private readonly IStartupService startupService;

    public SettingsViewModel(ISettingsService settingsService, IStartupService startupService)
    {
        this.settingsService = settingsService;
        this.startupService = startupService;
    }

    public bool StartWithWindows => startupService.IsEnabled;
    public bool CloseToTray => settingsService.Current.CloseToTray;
    public string DiscordApplicationId => settingsService.Current.DiscordApplicationId;
    public bool IsDiscordApplicationIdConfigured => !string.IsNullOrWhiteSpace(DiscordApplicationId);

    public void SetStartWithWindows(bool enabled)
    {
        startupService.SetEnabled(enabled);
    }

    public void SetCloseToTray(bool enabled)
    {
        settingsService.Update(settingsService.Current with { CloseToTray = enabled });
    }

    public void SetDiscordApplicationId(string applicationId)
    {
        settingsService.Update(settingsService.Current with
        {
            DiscordApplicationId = applicationId.Trim()
        });
    }

    public void ClearDiscordApplicationId()
    {
        SetDiscordApplicationId(string.Empty);
    }
}
