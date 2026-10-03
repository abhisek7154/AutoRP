using AutoRP.Models;
using Microsoft.Extensions.Logging;

namespace AutoRP.Services;

public sealed class StartupService : IStartupService
{
    private readonly ISettingsService settingsService;
    private readonly IStartupRegistration registration;
    private readonly ILogger<StartupService> logger;

    public StartupService(
        ISettingsService settingsService,
        IStartupRegistration registration,
        ILogger<StartupService> logger)
    {
        this.settingsService = settingsService;
        this.registration = registration;
        this.logger = logger;
    }

    public bool IsEnabled => settingsService.Current.StartWithWindows;

    public void SetEnabled(bool enabled)
    {
        try
        {
            if (registration.IsRegistered != enabled)
            {
                registration.SetRegistered(enabled);
            }

            settingsService.Update(settingsService.Current with { StartWithWindows = enabled });
            logger.LogInformation("Start with Windows {State}.", enabled ? "enabled" : "disabled");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to update Start with Windows setting.");
            throw;
        }
    }
}
