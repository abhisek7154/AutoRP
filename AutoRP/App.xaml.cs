using AutoRP.Configuration;
using AutoRP.Services;
using AutoRP.UI;
using AutoRP.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace AutoRP;

public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        serviceProvider = services.BuildServiceProvider();

        MainWindow = serviceProvider.GetRequiredService<MainWindow>();
        var trayService = serviceProvider.GetRequiredService<ITrayService>();
        trayService.OpenRequested += OnTrayOpenRequested;
        trayService.ExitRequested += OnTrayExitRequested;
        MainWindow.Show();
        trayService.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.GetService<PresenceCoordinator>()?.StopAsync().GetAwaiter().GetResult();
        serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton(new AutoRpOptions());
        services.AddSingleton<IActiveWindowService, WindowsActiveWindowService>();
        services.AddSingleton<IProfileConfigurationService, JsonProfileConfigurationService>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IStartupRegistration, RegistryStartupRegistration>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<IPresenceProfileManager, PresenceProfileManager>();
        services.AddSingleton<IDiscordRpcService, DiscordRpcService>();
        services.AddSingleton<AutoSwitchService>();
        services.AddSingleton<ProfileManagementViewModel>();
        services.AddSingleton<PresenceCoordinator>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<ITrayService, TrayService>();
    }

    private void OnTrayOpenRequested(object? sender, EventArgs e)
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        MainWindow?.Close();
        Shutdown();
    }
}
