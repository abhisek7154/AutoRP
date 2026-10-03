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
    private int shutdownStarted;

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
        if (Interlocked.Exchange(ref shutdownStarted, 1) == 0)
        {
            ShutdownServicesAsync().GetAwaiter().GetResult();
        }

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

    private async void OnTrayExitRequested(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await ShutdownServicesAsync();
        }
        finally
        {
            Shutdown();
        }
    }

    private async Task ShutdownServicesAsync()
    {
        var provider = serviceProvider;
        if (provider is null)
        {
            return;
        }

        var window = MainWindow;
        if (window is not null)
        {
            foreach (var ownedWindow in window.OwnedWindows.OfType<Window>().ToArray())
            {
                ownedWindow.Close();
            }

            window.Close();
        }

        try
        {
            var coordinator = provider.GetService<PresenceCoordinator>();
            if (coordinator is not null)
            {
                await coordinator.StopAsync();
            }
        }
        finally
        {
            var trayService = provider.GetService<ITrayService>();
            if (trayService is not null)
            {
                trayService.OpenRequested -= OnTrayOpenRequested;
                trayService.ExitRequested -= OnTrayExitRequested;
                trayService.Dispose();
            }

            provider.Dispose();
            serviceProvider = null;
        }
    }
}
