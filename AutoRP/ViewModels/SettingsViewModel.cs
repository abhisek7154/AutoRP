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

    public void SetStartWithWindows(bool enabled)
    {
        startupService.SetEnabled(enabled);
    }

    public void SetCloseToTray(bool enabled)
    {
        settingsService.Update(settingsService.Current with { CloseToTray = enabled });
    }
}
