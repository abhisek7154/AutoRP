using Hardcodet.Wpf.TaskbarNotification;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AutoRP.Services;

public sealed class TrayService : ITrayService
{
    private readonly AutoSwitchService autoSwitchService;
    private readonly IDiscordRpcService discordRpcService;
    private TaskbarIcon? taskbarIcon;
    private MenuItem? pauseItem;
    private MenuItem? resumeItem;
    private MenuItem? statusItem;
    private MenuItem? applicationItem;
    private MenuItem? profileItem;
    private MenuItem? discordItem;
    private bool isDisposed;

    public TrayService(AutoSwitchService autoSwitchService, IDiscordRpcService discordRpcService)
    {
        this.autoSwitchService = autoSwitchService;
        this.discordRpcService = discordRpcService;
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
        pauseItem.Click += (_, _) => autoSwitchService.SetEnabled(false);
        resumeItem = new MenuItem { Header = "Resume Automatic Switching" };
        resumeItem.Click += (_, _) => autoSwitchService.SetEnabled(true);
        statusItem = new MenuItem { Header = "AutoRP - Running", IsEnabled = false };
        applicationItem = new MenuItem { Header = "Application: detecting...", IsEnabled = false };
        profileItem = new MenuItem { Header = "Profile: none", IsEnabled = false };
        discordItem = new MenuItem { Header = "Discord: Disconnected", IsEnabled = false };

        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "Open AutoRP" };
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var clearItem = new MenuItem { Header = "Clear Discord Presence" };
        clearItem.Click += async (_, _) =>
        {
            autoSwitchService.MarkPresenceCleared();
            await discordRpcService.ClearPresenceAsync();
        };
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += OnExitClick;
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(pauseItem);
        menu.Items.Add(resumeItem);
        menu.Items.Add(clearItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(statusItem);
        menu.Items.Add(applicationItem);
        menu.Items.Add(profileItem);
        menu.Items.Add(discordItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        taskbarIcon = new TaskbarIcon
        {
            IconSource = CreateIconSource(),
            ToolTipText = "AutoRP",
            ContextMenu = menu,
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
        if (taskbarIcon is not null)
        {
            taskbarIcon.Visibility = Visibility.Hidden;
            taskbarIcon.Dispose();
            taskbarIcon = null;
        }

    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        IsExitRequested = true;
        ExitRequested?.Invoke(this, EventArgs.Empty);
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
