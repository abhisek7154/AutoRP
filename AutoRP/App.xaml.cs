using AutoRP.Configuration;
using AutoRP.Services;
using AutoRP.UI;
using AutoRP.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Threading;

namespace AutoRP;

public partial class App : Application
{
    private ServiceProvider? serviceProvider;
    private ApplicationShutdownCoordinator? shutdownCoordinator;
    private ILogger<App>? logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        serviceProvider = services.BuildServiceProvider();
        logger = serviceProvider.GetRequiredService<ILogger<App>>();
        shutdownCoordinator = new ApplicationShutdownCoordinator(ShutdownServicesAsync, ShutdownOnDispatcher, logger);

        MainWindow = serviceProvider.GetRequiredService<MainWindow>();
        var trayService = serviceProvider.GetRequiredService<ITrayService>();
        trayService.OpenRequested += OnTrayOpenRequested;
        trayService.ExitRequested += OnTrayExitRequested;
        MainWindow.Show();
        trayService.Start();

        // Allows the exact packaged executable to exercise the production tray-exit event
        // path when desktop automation cannot reach the notification area.
        if (e.Args.Contains("--test-tray-exit", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(trayService.RequestExit));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        logger?.LogInformation("WPF application exit completed.");
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
        logger?.LogInformation("Tray Exit clicked.");
        try
        {
            await ShutdownApplicationAsync();
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Shutdown coordinator failed.");
            ShutdownOnDispatcher();
        }
    }

    public Task ShutdownApplicationAsync() => shutdownCoordinator?.ShutdownAsync() ?? Task.CompletedTask;

    private async Task ShutdownServicesAsync()
    {
        var provider = serviceProvider;
        if (provider is null)
        {
            return;
        }

        try
        {
            var trayService = provider.GetService<ITrayService>();
            if (trayService is not null)
            {
                trayService.OpenRequested -= OnTrayOpenRequested;
                trayService.ExitRequested -= OnTrayExitRequested;
                TryCleanup("tray resources", trayService.Dispose);
            }
        }
        finally
        {
            try
            {
                var window = MainWindow;
                if (window is not null)
                {
                    foreach (var ownedWindow in window.OwnedWindows.OfType<Window>().ToArray())
                    {
                        TryCleanup($"owned window '{ownedWindow.Title}'", ownedWindow.Close);
                    }

                    if (window.IsLoaded)
                    {
                        TryCleanup("main window", window.Close);
                    }
                }
            }
            finally
            {
                try
                {
                    var coordinator = provider.GetService<PresenceCoordinator>();
                    if (coordinator is not null)
                    {
                        logger?.LogInformation("Stopping foreground monitor and automatic switching.");
                        await TryCleanupAsync("presence services", () => coordinator.StopAsync());
                    }
                }
                finally
                {
                    try
                    {
                        logger?.LogInformation("Disposing application services.");
                        await provider.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        logger?.LogError(exception, "Application service disposal failed.");
                    }
                    finally
                    {
                        serviceProvider = null;
                    }
                }
            }
        }
    }

    private void ShutdownOnDispatcher()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            Shutdown();
            return;
        }

        Dispatcher.InvokeAsync(Shutdown, DispatcherPriority.Send);
    }

    private void TryCleanup(string name, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Failed to clean up {Resource} during shutdown.", name);
        }
    }

    private async Task TryCleanupAsync(string name, Func<Task> cleanup)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Failed to clean up {Resource} during shutdown.", name);
        }
    }
}
