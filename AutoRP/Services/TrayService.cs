using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AutoRP.Services;

public sealed class TrayService : ITrayService
{
    private readonly AutoSwitchService autoSwitchService;
    private readonly IDiscordRpcService discordRpcService;
    private readonly ILogger<TrayService>? logger;
    private TaskbarIcon? taskbarIcon;
    private ContextMenu? contextMenu;
    private MenuItem? openItem;
    private MenuItem? pauseItem;
    private MenuItem? resumeItem;
    private MenuItem? statusItem;
    private MenuItem? applicationItem;
    private MenuItem? profileItem;
    private MenuItem? discordItem;
    private MenuItem? clearItem;
    private MenuItem? exitItem;
    private bool isDisposed;

    public TrayService(
        AutoSwitchService autoSwitchService,
        IDiscordRpcService discordRpcService,
        ILogger<TrayService>? logger = null)
    {
        this.autoSwitchService = autoSwitchService;
        this.discordRpcService = discordRpcService;
        this.logger = logger;
        autoSwitchService.StateChanged += OnStateChanged;
        discordRpcService.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;
    public bool IsExitRequested { get; private set; }

    public void Start()
    {
        if (taskbarIcon is not null)
        {
            return;
        }

        pauseItem = new MenuItem { Header = "Pause Automatic Switching" };
        pauseItem.Click += OnPauseClick;
        resumeItem = new MenuItem { Header = "Resume Automatic Switching" };
        resumeItem.Click += OnResumeClick;
        statusItem = new MenuItem { Header = "AutoRP - Running", IsEnabled = false };
        applicationItem = new MenuItem { Header = "Application: detecting...", IsEnabled = false };
        profileItem = new MenuItem { Header = "Profile: none", IsEnabled = false };
        discordItem = new MenuItem { Header = "Discord: Disconnected", IsEnabled = false };

        contextMenu = new ContextMenu { StaysOpen = false };
        openItem = new MenuItem { Header = "Open AutoRP" };
        openItem.Click += OnOpenClick;
        clearItem = new MenuItem { Header = "Clear Discord Presence" };
        clearItem.Click += OnClearPresenceClick;
        exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += OnExitClick;
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(pauseItem);
        contextMenu.Items.Add(resumeItem);
        contextMenu.Items.Add(clearItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(statusItem);
        contextMenu.Items.Add(applicationItem);
        contextMenu.Items.Add(profileItem);
        contextMenu.Items.Add(discordItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        taskbarIcon = new TaskbarIcon
        {
            IconSource = CreateIconSource(),
            ToolTipText = "AutoRP",
            ContextMenu = contextMenu,
            Visibility = Visibility.Visible
        };
        taskbarIcon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        UpdateStatus();
    }

    public void UpdateStatus()
    {
        if (taskbarIcon is null)
        {
            return;
        }

        void update()
        {
            if (statusItem is null || pauseItem is null || resumeItem is null || applicationItem is null || profileItem is null || discordItem is null)
            {
                return;
            }

            statusItem.Header = "AutoRP - Running";
            pauseItem.IsEnabled = autoSwitchService.IsEnabled;
            resumeItem.IsEnabled = !autoSwitchService.IsEnabled;
            var application = autoSwitchService.CurrentApplication;
            applicationItem.Header = $"Application: {application?.ExecutableName ?? "none"}";
            profileItem.Header = $"Profile: {autoSwitchService.MatchedProfile?.Name ?? "none"}";
            discordItem.Header = $"Discord: {(discordRpcService.IsConnected ? "Connected" : "Disconnected")}";
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            update();
        }
        else
        {
            Application.Current?.Dispatcher.BeginInvoke(update);
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        autoSwitchService.StateChanged -= OnStateChanged;
        discordRpcService.ConnectionStatusChanged -= OnConnectionStatusChanged;
        if (contextMenu is not null)
        {
            contextMenu.IsOpen = false;
            if (openItem is not null)
            {
                openItem.Click -= OnOpenClick;
            }

            if (pauseItem is not null)
            {
                pauseItem.Click -= OnPauseClick;
            }

            if (resumeItem is not null)
            {
                resumeItem.Click -= OnResumeClick;
            }

            if (clearItem is not null)
            {
                clearItem.Click -= OnClearPresenceClick;
            }

            if (exitItem is not null)
            {
                exitItem.Click -= OnExitClick;
            }

            contextMenu.Items.Clear();
        }

        if (taskbarIcon is not null)
        {
            taskbarIcon.ContextMenu = null;
            taskbarIcon.Visibility = Visibility.Hidden;
            taskbarIcon.Dispose();
            taskbarIcon = null;
        }

        contextMenu = null;
        openItem = null;
        pauseItem = null;
        resumeItem = null;
        statusItem = null;
        applicationItem = null;
        profileItem = null;
        discordItem = null;
        clearItem = null;
        exitItem = null;
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        logger?.LogInformation("Tray Exit menu item clicked.");
        RequestExit();
    }

    public void RequestExit()
    {
        if (IsExitRequested)
        {
            return;
        }

        CloseContextMenu();
        IsExitRequested = true;
        logger?.LogInformation("Tray exit request dispatched after closing the context menu.");
        // Let WPF finish the routed click and close the native popup before teardown.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ExitRequested?.Invoke(this, EventArgs.Empty)));
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        CloseContextMenu();
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        CloseContextMenu();
        autoSwitchService.SetEnabled(false);
    }

    private void OnResumeClick(object sender, RoutedEventArgs e)
    {
        CloseContextMenu();
        autoSwitchService.SetEnabled(true);
    }

    private async void OnClearPresenceClick(object sender, RoutedEventArgs e)
    {
        CloseContextMenu();
        autoSwitchService.MarkPresenceCleared();
        await discordRpcService.ClearPresenceAsync();
    }

    private void CloseContextMenu()
    {
        if (contextMenu is not null)
        {
            contextMenu.IsOpen = false;
        }
    }

    private void OnStateChanged(object? sender, AutoSwitchStateChangedEventArgs e)
    {
        UpdateStatus();
    }

    private void OnConnectionStatusChanged(object? sender, DiscordConnectionStatusChangedEventArgs e)
    {
        UpdateStatus();
    }

    private BitmapSource CreateIconSource()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri("pack://application:,,,/AutoRP;component/Resources/AutoRP.ico", UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
