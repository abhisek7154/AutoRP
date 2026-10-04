# AutoRP

<img width="1312" height="1199" alt="AutoRP" src="https://github.com/user-attachments/assets/4abd127e-8dda-41b3-b466-e83606e9e850" />

AutoRP automatically changes your Discord Rich Presence based on the Windows application you're using.

## Quick Start

1. Download the AutoRP Windows ZIP, extract it, and run `AutoRP.exe`.
2. Start Discord.
3. Open **Settings** in AutoRP.
4. Enter your Discord Application ID and click **Save**.
5. Choose a profile for the app you use, or click **Add Profile** to create one.
6. Upload the matching Discord Rich Presence Art Asset if you want custom artwork.
7. Turn on **Automatic Switching**.
8. Switch to Firefox, VS Code, Spotify, or another profiled app. AutoRP updates your Discord presence automatically.

`Open AutoRP → Configure Discord → Create Profile → Enable Automatic Switching → Switch apps → Discord updates`

## First Time Using AutoRP?

- You do **not** need to edit JSON by hand.
- Built-in profiles cover common apps, so you do not need to look up executable names for those apps.
- AutoRP shows the current application; use it as a guide when creating a profile for an app.
- The advanced profile editor is there when you need process, window-title, or priority matching.
- Settings and profiles are stored locally on your computer.

For example:

`Firefox → Browsing with Firefox → Firefox artwork`

`VS Code → Coding → VS Code artwork`

AutoRP also changes the activity name shown in Discord to match the selected profile. Browser tabs for Netflix, Crunchyroll, and YouTube Music use the browser process together with the window title, so they can switch independently from general Firefox or Chrome browsing. Upload artwork with the matching keys (`netflix`, `crunchyroll`, or `youtube_music`) if you want those images; asset keys only display artwork after you upload them to your Discord application.

## Features

- Foreground application detection through Windows APIs with low-frequency polling.
- Process matching by executable name or process name, case-insensitively.
- Multiple-process profiles through `ProcessNames`.
- Window-title matching through `WindowTitleContains`.
- Priority ordering with configuration order used for ties.
- Automatic switching with duplicate-update suppression and stale-transition protection.
- Live profile add, edit, delete, enable, disable, and test operations.
- Discord reconnect retry and current-profile reapplication.
- System-tray operation, close-to-tray, explicit exit, and status updates.
- Optional per-user Windows startup registration through `HKCU`.
- Resilient JSON persistence with malformed-file preservation and atomic saves.

## Architecture

The automatic switching flow is:

```text
Windows Active Window Detection
	↓
Presence Profile Manager
	↓
AutoSwitch Service
	↓
Discord RPC Service
```

- **Models** contains `ActiveApplication`, `RpcProfile`, `RpcButton`, and `AutoRpSettings`.
- **Configuration** contains `AutoRpOptions`, including the default profiles and polling interval.
- **Services** contains the application behavior: `WindowsActiveWindowService` detects the foreground window, `PresenceProfileManager` selects profiles, `AutoSwitchService` coordinates transitions, `DiscordRpcService` owns the Discord client, and `PresenceCoordinator` connects the UI to those services. `JsonProfileConfigurationService` and `JsonSettingsService` persist user data. `TrayService` owns the notification icon, while `StartupService` and `RegistryStartupRegistration` manage Windows startup.
- **ViewModels** contains profile and settings presentation logic.
- **UI** contains the WPF `MainWindow` and `ProfileEditorWindow`.
- **Resources** contains the WPF theme and `AutoRP.ico`.
- **Tests** contains observable behavior tests for matching, switching, persistence, startup settings, and reliability cases.

## Requirements

- Windows 10 or newer.
- .NET 8 SDK with Windows desktop targeting support.
- Discord desktop client for live Rich Presence.

## Discord Application ID Setup

1. Open the [Discord Developer Portal](https://discord.com/developers/applications).
2. Select **New Application**, name it, and create it.
3. Open **General Information** and copy the **Application ID**.
4. Enter the ID in AutoRP's **Settings > Discord Application ID** field and select **Save**. AutoRP stores it in `%LOCALAPPDATA%\AutoRP\settings.json` and reloads it on later launches. **Clear** removes the saved value.

The environment variable remains available as a fallback for scripted or first-time launches:

   ```powershell
   $env:AUTORP_DISCORD_APPLICATION_ID = "your-application-id"
   ```

The saved UI value takes priority over `AUTORP_DISCORD_APPLICATION_ID`. Never commit an Application ID, token, secret, or machine-specific configuration. A missing Application ID is supported; AutoRP remains open and reports `Discord Application ID not configured.` without publishing presence.

## Discord Artwork

AutoRP uses Discord-hosted asset keys. The seeded profiles use `firefox`, `vscode`, `spotify`, `chrome`, `netflix`, `crunchyroll`, and `youtube_music`; the sample presence uses `autorp_anime`. These are keys only; upload each image to your Discord application's Art Assets before expecting it to appear:

1. Open the [Discord Developer Portal](https://discord.com/developers/applications).
2. Open the AutoRP application.
3. Go to **Rich Presence** / **Art Assets**.
4. Upload images for the keys used by your profiles.
5. Set each asset name/key exactly to the value entered in the profile editor.
6. Configure the Application ID in AutoRP as described above.
7. Start AutoRP and verify the artwork appears in Discord Rich Presence.

The asset key must exactly match the name uploaded in the Developer Portal. Existing or edited profiles can provide `LargeImageKey`, `LargeImageText`, `SmallImageKey`, and `SmallImageText`. AutoRP never uploads local images; Discord serves the uploaded asset.

## Running From Source

```powershell
dotnet restore
dotnet build .\AutoRP.sln
dotnet run --project .\AutoRP\AutoRP.csproj
```

## Tests

```powershell
dotnet test .\AutoRP\Tests\AutoRP.Tests.csproj
```

The test suite uses fakes for Windows and Discord boundaries and verifies observable behavior rather than implementation details.

## User Configuration

AutoRP stores user-owned data under:

```text
%LOCALAPPDATA%\AutoRP
```

The profile file is `profiles.json` and the settings file is `settings.json`. Missing files and directories are handled automatically. Malformed files are backed up with an `.invalid-` suffix, valid existing files are not overwritten during load, and saves use a temporary file followed by replacement.

The JSON profile schema remains compatible with both the legacy singular `ProcessName` property and the newer `ProcessNames` and `WindowTitleContains` properties.

## Profile Configuration

Profiles are edited from the application UI and contain fields represented by `RpcProfile`, including:

- `Name`: unique display name.
- `ProcessName`: legacy/single process criterion.
- `ProcessNames`: one or more executable or process names.
- `WindowTitleContains`: one or more case-insensitive title fragments.
- `Priority`: larger values win when several profiles match.
- `Details`, `State`, and optional image/button fields for Discord presence.
- `IsEnabled`: disabled profiles are ignored immediately.

At least one process criterion or title fragment is required. Deleting or disabling the active profile causes the current application to be re-evaluated and clears presence when no enabled profile matches.

## Automatic Switching Behavior

`WindowsActiveWindowService` polls the foreground window and ignores AutoRP itself. Changes are published only when the observed application changes. `PresenceProfileManager` filters enabled profiles and selects the highest-priority match. `AutoSwitchService` serializes Discord operations, suppresses equivalent updates, and versions transitions so rapid process or title changes cannot commit stale state.

### Process Matching

Matching is case-insensitive and checks both `ActiveApplication.ExecutableName` and `ActiveApplication.ProcessName`. For example, a profile containing `Code.exe` matches `code.exe` and the corresponding process name.

### Multiple-Process Matching

Set `ProcessNames` to a list such as `Code.exe` and `firefox.exe` when one presence should apply to several applications. The first matching profile is selected after priority ordering.

### Window-Title Matching

Set `WindowTitleContains` to fragments such as `ShriERP`. Every non-empty fragment must be present in the active window title. This allows two profiles for the same process to be selected based on the current document or window.

### Priority

Profiles are ordered by descending `Priority`. If priorities tie, their existing configuration order is preserved.

### Firefox Example

The default configuration includes a Firefox profile:

```text
ProcessName: firefox.exe
Details: Browsing with Firefox
State: Researching AutoRP
Priority: 20
```

When a Firefox window is foregrounded, the profile manager selects this profile unless a higher-priority enabled profile also matches.

## Discord Reconnects

`DiscordRpcService` owns connection serialization, duplicate presence suppression, cleanup, and periodic reconnect attempts. When Discord reconnects, `AutoSwitchService` reapplies the currently matched profile. If Discord is unavailable, AutoRP remains usable and reports the recoverable disconnected state.

## System Tray and Windows Startup

`TrayService` creates one notification-area icon and exposes Open, Pause, Resume, Clear Presence, and Exit commands. When **Close to system tray** is enabled, closing the main window hides it while monitoring continues. **Exit** stops monitoring, clears and disconnects Discord RPC, disposes the tray icon, and shuts down the application.

The **Start AutoRP with Windows** setting uses the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key. `StartupService` checks the existing registration before changing it, so repeated enable/disable operations are idempotent.

## Troubleshooting

- **Discord is disconnected:** Start the Discord desktop client, verify `AUTORP_DISCORD_APPLICATION_ID`, and use Connect or wait for automatic retry.
- **No profile matches:** Check the process executable name, title fragments, `IsEnabled`, and priority in the profile editor.
- **Configuration will not load:** Inspect `%LOCALAPPDATA%\AutoRP` for the original file and its `.invalid-` backup, then correct the JSON or use the application editor.
- **Startup does not run:** Confirm the Windows startup setting is enabled and that the current user's Run key is accessible.
- **AutoRP cannot inspect a process:** Protected or short-lived Windows processes may be unavailable; detection continues without terminating AutoRP.

## Release Build

Build and test before publishing:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:USERPROFILE\.dotnet\tools;$env:PATH"
dotnet build .\AutoRP.sln --configuration Release --nologo
dotnet test .\AutoRP\Tests\AutoRP.Tests.csproj --no-restore --nologo
```

Create a self-contained Windows x64 release folder:

```powershell
dotnet publish .\AutoRP\AutoRP.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output .\artifacts\AutoRP-win-x64 `
  --nologo
```

The application icon is `AutoRP\Resources\AutoRP.ico`. Do not copy profiles, settings, build output, tests, secrets, or other user-specific data into a release package.
# AutoRP

AutoRP is a Windows WPF desktop application that updates Discord Rich Presence from the active Windows application. It supports process and window-title matching, profile priorities, live profile management, automatic switching, system-tray operation, Discord reconnects, and Windows startup registration.

## Release build

Build and test the solution before packaging:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:USERPROFILE\.dotnet\tools;$env:PATH"
dotnet build .\AutoRP.sln --configuration Release --nologo
dotnet test .\AutoRP\Tests\AutoRP.Tests.csproj --no-restore --nologo
```

Create a self-contained Windows x64 release folder with:

```powershell
dotnet publish .\AutoRP\AutoRP.csproj `
	--configuration Release `
	--runtime win-x64 `
	--self-contained true `
	--output .\artifacts\AutoRP-win-x64 `
	--nologo
```

Copy the published folder to the target Windows machine. Configure the Application ID in the AutoRP Settings section after launch; the package includes the application icon and does not include user profiles, settings, tests, debug output, or secrets. User profiles and settings remain under `%LOCALAPPDATA%\AutoRP` and are not overwritten by an application update.

## Configure Discord Rich Presence

1. Open the [Discord Developer Portal](https://discord.com/developers/applications) and sign in.
2. Select **New Application**, enter `AutoRP` as the application name, and create it.
3. On the application's **General Information** page, copy the **Application ID**.
4. Enter the copied value in AutoRP's Settings section and select Save. For scripted launches, the environment variable remains a fallback:

	```powershell
	$env:AUTORP_DISCORD_APPLICATION_ID = "your-application-id"
	```

	The saved UI value takes priority over this environment variable, so the Application ID is not committed to source control.
5. In the application's **Rich Presence > Art Assets** page, upload any images used by a profile and use their asset keys for `LargeImageKey` or `SmallImageKey`.
6. Start Discord before AutoRP. Use **Set sample presence** to test the connection and **Clear presence** to remove it.

AutoRP treats a missing Application ID or a closed Discord client as a recoverable condition. It logs the condition and retries without crashing.

## Prerequisites

- Windows 10 or newer
- .NET 8 SDK
- Discord desktop client

## Run

Requires the .NET 8 SDK and Windows desktop targeting support.

```powershell
dotnet build .\AutoRP\AutoRP.csproj
dotnet run --project .\AutoRP\AutoRP.csproj
```

## Test Rich Presence

With Discord running and the Application ID configured, launch AutoRP and use **Connect**. The connection status appears in the window. Use **Set Test Presence** to publish an obvious sample status, then use **Clear Presence** to remove it. **Disconnect** closes the RPC connection cleanly.

If Discord is not running, AutoRP remains open, reports a disconnected status, logs the unavailable connection, and retries automatically. Start Discord and use **Connect** again, or wait for the retry to succeed.

## Foreground application detection

AutoRP polls the Windows foreground window at the configured interval and updates the detection panel when the focused application changes. It displays the process name, executable name, process ID, and window title. AutoRP itself is ignored, and short-lived or protected processes are handled without terminating the application.

## Automatic switching

Automatic switching is enabled by default. Sample profiles are defined in `AutoRpOptions.Profiles` for `Code.exe`, `chrome.exe`, `firefox.exe`, and `Spotify.exe`; edit or remove these entries to customize matching. Matching is case-insensitive and uses the executable name first, then the process name.

The UI shows the current matched profile, last switch time, and any application/Discord status message. Disable automatic switching to prevent foreground changes from changing Discord presence. Manual Connect, Disconnect, Set Test Presence, and Clear Presence controls remain available in either mode.

## System tray and Windows startup

AutoRP creates a notification-area icon while running. Closing the window hides it to the tray when **Close to system tray** is enabled. Use **Open AutoRP** in the tray menu to restore it, or use **Exit** to stop monitoring, clear/disconnect Discord RPC, dispose the tray icon, and shut down.

The tray menu also exposes **Pause Automatic Switching**, **Resume Automatic Switching**, and **Clear Discord Presence**. Pause and resume call the existing automatic-switching service.

The **Start AutoRP with Windows** setting uses the current user's `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` entry, so administrator privileges are not required. The preference is stored in `%LOCALAPPDATA%\\AutoRP\\settings.json`; enabling it is safe to repeat and disabling it removes the entry.
