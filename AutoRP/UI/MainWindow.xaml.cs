using AutoRP.Models;
using AutoRP.Services;
using AutoRP.ViewModels;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AutoRP.UI;

public partial class MainWindow : Window
{
    private readonly PresenceCoordinator presenceCoordinator;
    private readonly ILogger<MainWindow> logger;
    private readonly DispatcherTimer refreshTimer;
    private readonly ProfileManagementViewModel profileManagementViewModel;
    private readonly ITrayService trayService;
    private readonly SettingsViewModel settingsViewModel;

    public MainWindow(
        PresenceCoordinator presenceCoordinator,
        ProfileManagementViewModel profileManagementViewModel,
        ITrayService trayService,
        SettingsViewModel settingsViewModel,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();
        this.presenceCoordinator = presenceCoordinator;
        this.profileManagementViewModel = profileManagementViewModel;
        this.trayService = trayService;
        this.settingsViewModel = settingsViewModel;
        this.logger = logger;
        DataContext = profileManagementViewModel;
        AutomaticSwitchingCheckBox.Checked += OnAutomaticSwitchingChanged;
        AutomaticSwitchingCheckBox.Unchecked += OnAutomaticSwitchingChanged;
        StartWithWindowsCheckBox.Checked += OnStartWithWindowsChanged;
        StartWithWindowsCheckBox.Unchecked += OnStartWithWindowsChanged;
        CloseToTrayCheckBox.Checked += OnCloseToTrayChanged;
        CloseToTrayCheckBox.Unchecked += OnCloseToTrayChanged;

        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        refreshTimer.Tick += OnRefreshTimerTick;
        Loaded += OnLoaded;
        Closed += OnClosed;
        presenceCoordinator.AutomaticSwitchingStateChanged += OnAutomaticSwitchingStateChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await presenceCoordinator.StartAsync();
        RefreshAutomaticSwitchingState();
        RefreshSettings();
        RefreshRpcStatus();
        refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        refreshTimer.Stop();
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        AutomaticSwitchingCheckBox.Checked -= OnAutomaticSwitchingChanged;
        AutomaticSwitchingCheckBox.Unchecked -= OnAutomaticSwitchingChanged;
        StartWithWindowsCheckBox.Checked -= OnStartWithWindowsChanged;
        StartWithWindowsCheckBox.Unchecked -= OnStartWithWindowsChanged;
        CloseToTrayCheckBox.Checked -= OnCloseToTrayChanged;
        CloseToTrayCheckBox.Unchecked -= OnCloseToTrayChanged;
        presenceCoordinator.AutomaticSwitchingStateChanged -= OnAutomaticSwitchingStateChanged;
        if (trayService.IsExitRequested)
        {
            return;
        }

        if (!settingsViewModel.CloseToTray)
        {
            Application.Current.Shutdown();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (trayService.IsExitRequested || !settingsViewModel.CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private async void OnSetSamplePresenceClick(object sender, RoutedEventArgs e)
    {
        await RunRpcActionAsync(() => presenceCoordinator.SetSamplePresenceAsync());
    }

    private async void OnClearPresenceClick(object sender, RoutedEventArgs e)
    {
        await RunRpcActionAsync(() => presenceCoordinator.ClearPresenceAsync());
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        await RunRpcActionAsync(() => presenceCoordinator.ConnectDiscordAsync());
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        await RunRpcActionAsync(() => presenceCoordinator.DisconnectDiscordAsync());
    }

    private void OnAutomaticSwitchingChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.IsChecked is bool enabled)
        {
            presenceCoordinator.SetAutomaticSwitchingEnabled(enabled);
        }
    }

    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.IsChecked is bool enabled)
        {
            if (enabled != settingsViewModel.StartWithWindows)
            {
                RunSettingsAction(() => settingsViewModel.SetStartWithWindows(enabled));
            }
        }
    }

    private void OnCloseToTrayChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.IsChecked is bool enabled)
        {
            if (enabled != settingsViewModel.CloseToTray)
            {
                RunSettingsAction(() => settingsViewModel.SetCloseToTray(enabled));
            }
        }
    }

    private void OnSaveDiscordApplicationIdClick(object sender, RoutedEventArgs e)
    {
        RunSettingsAction(() => settingsViewModel.SetDiscordApplicationId(DiscordApplicationIdTextBox.Text));
    }

    private void OnClearDiscordApplicationIdClick(object sender, RoutedEventArgs e)
    {
        RunSettingsAction(settingsViewModel.ClearDiscordApplicationId);
    }

    private void OnAddProfileClick(object sender, RoutedEventArgs e)
    {
        var editor = new ProfileEditorWindow(new ProfileEditorViewModel()) { Owner = this };
        if (editor.ShowDialog() == true && editor.Profile is not null)
        {
            RunProfileAction(() => profileManagementViewModel.AddProfile(editor.Profile));
        }
    }

    private void OnEditProfileClick(object sender, RoutedEventArgs e)
    {
        var selected = profileManagementViewModel.SelectedProfile;
        if (selected is null)
        {
            ShowProfileMessage("Select a profile to edit.");
            return;
        }

        var editor = new ProfileEditorWindow(new ProfileEditorViewModel(selected.Profile)) { Owner = this };
        if (editor.ShowDialog() == true && editor.Profile is not null)
        {
            RunProfileAction(() => profileManagementViewModel.UpdateProfile(selected.Name, editor.Profile));
        }
    }

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        var selected = profileManagementViewModel.SelectedProfile;
        if (selected is null)
        {
            ShowProfileMessage("Select a profile to delete.");
            return;
        }

        var result = MessageBox.Show(this, $"Delete profile '{selected.Name}'?", "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            RunProfileAction(() => profileManagementViewModel.DeleteSelectedProfile());
        }
    }

    private void OnToggleProfileClick(object sender, RoutedEventArgs e)
    {
        var selected = profileManagementViewModel.SelectedProfile;
        if (selected is null)
        {
            ShowProfileMessage("Select a profile to enable or disable.");
            return;
        }

        RunProfileAction(() => profileManagementViewModel.SetSelectedProfileEnabled(!selected.Profile.IsEnabled));
    }

    private async void OnTestProfileClick(object sender, RoutedEventArgs e)
    {
        await RunRpcActionAsync(() => profileManagementViewModel.TestSelectedProfileAsync());
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        RefreshRpcStatus();
        RefreshAutomaticSwitchingState();
    }

    private void OnAutomaticSwitchingStateChanged(object? sender, AutoSwitchStateChangedEventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            Dispatcher.BeginInvoke(RefreshAutomaticSwitchingState);
        }
    }

    private void RefreshAutomaticSwitchingState()
    {
        UpdateApplicationDisplay(presenceCoordinator.CurrentApplication);
        MatchedProfileText.Text = presenceCoordinator.MatchedProfile?.Details ?? "No matched profile";
        SwitchStatusText.Text = presenceCoordinator.AutomaticSwitchingStatus;
        LastSwitchText.Text = presenceCoordinator.LastSwitchTime is { } lastSwitch
            ? $"Last switch: {lastSwitch.LocalDateTime:g}"
            : "Last switch: never";
        AutomaticSwitchingCheckBox.IsChecked = presenceCoordinator.IsAutomaticSwitchingEnabled;
        AutomaticSwitchingCheckBox.Content = presenceCoordinator.IsAutomaticSwitchingEnabled ? "ON" : "OFF";
    }

    private void UpdateApplicationDisplay(ActiveApplication? application)
    {
        if (application is null)
        {
            DetectionStatusText.Text = "No supported foreground application detected";
            ActiveApplicationText.Text = "-";
            ProcessText.Text = "-";
            ProcessIdText.Text = "-";
            WindowTitleText.Text = "-";
            return;
        }

        DetectionStatusText.Text = "Foreground application detected";
        ActiveApplicationText.Text = application.ProcessName;
        ProcessText.Text = application.ExecutableName;
        ProcessIdText.Text = application.ProcessId.ToString();
        WindowTitleText.Text = string.IsNullOrWhiteSpace(application.WindowTitle) ? "(Untitled window)" : application.WindowTitle;
    }

    private void RefreshRpcStatus()
    {
        RpcStatusText.Text = presenceCoordinator.IsDiscordConnected
            ? "Connected"
            : presenceCoordinator.IsDiscordApplicationIdConfigured
                ? "Disconnected"
                : "Discord Application ID not configured.";
    }

    private void RefreshSettings()
    {
        StartWithWindowsCheckBox.IsChecked = settingsViewModel.StartWithWindows;
        CloseToTrayCheckBox.IsChecked = settingsViewModel.CloseToTray;
        DiscordApplicationIdTextBox.Text = settingsViewModel.DiscordApplicationId;
        DiscordApplicationIdStatusText.Text = presenceCoordinator.IsDiscordApplicationIdConfigured
            ? "Status: Configured"
            : "Status: Not configured";
    }

    private async Task RunRpcActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Discord RPC UI action failed.");
        }
        finally
        {
            RefreshRpcStatus();
            RefreshAutomaticSwitchingState();
        }
    }

    private void RunProfileAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Profile management action failed.");
            ShowProfileMessage(exception.Message);
        }
    }

    private void ShowProfileMessage(string message)
    {
        MessageBox.Show(this, message, "RPC Profiles", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RunSettingsAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Settings update failed.");
            ShowProfileMessage(exception.Message);
            RefreshSettings();
        }
    }
}
