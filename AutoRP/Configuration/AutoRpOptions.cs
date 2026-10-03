using AutoRP.Models;

namespace AutoRP.Configuration;

public sealed class AutoRpOptions
{
    public string DiscordApplicationId { get; init; } =
        Environment.GetEnvironmentVariable("AUTORP_DISCORD_APPLICATION_ID") ?? string.Empty;
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(2);

    public IReadOnlyList<RpcProfile> Profiles { get; init; } =
    [
        new RpcProfile(
            ProcessName: "Code.exe",
            Details: "Coding in Visual Studio Code",
            State: "Building AutoRP",
            LargeImageKey: "vscode",
            LargeImageText: "Visual Studio Code")
        {
            Name = "Visual Studio Code",
            ActivityName = "Visual Studio Code",
            ProcessNames = ["Code.exe"],
            Priority = 10
        },
        new RpcProfile(
            ProcessName: "chrome.exe",
            Details: "Browsing with Chrome",
            State: "Researching AutoRP",
            LargeImageKey: "chrome",
            LargeImageText: "Google Chrome")
        {
            Name = "Google Chrome",
            ActivityName = "Google Chrome",
            ProcessNames = ["chrome.exe"],
            Priority = 10
        },
        new RpcProfile(
            ProcessName: "firefox.exe",
            Details: "Browsing with Firefox",
            State: "Researching AutoRP",
            LargeImageKey: "firefox",
            LargeImageText: "Mozilla Firefox")
        {
            Name = "Mozilla Firefox",
            ActivityName = "Firefox",
            ProcessNames = ["firefox.exe"],
            Priority = 20
        },
        new RpcProfile(
            ProcessName: "Spotify.exe",
            Details: "Listening on Spotify",
            State: "Enjoying music",
            LargeImageKey: "spotify",
            LargeImageText: "Spotify")
        {
            Name = "Spotify",
            ActivityName = "Spotify",
            ProcessNames = ["Spotify.exe"],
            Priority = 10
        },
        new RpcProfile(
            ProcessName: "",
            Details: "Browsing Netflix",
            State: "Watching Netflix",
            LargeImageKey: "netflix",
            LargeImageText: "Netflix")
        {
            Name = "Netflix",
            ActivityName = "Netflix",
            ProcessNames = ["firefox.exe", "chrome.exe"],
            WindowTitleContains = ["Netflix"],
            Priority = 100
        },
        new RpcProfile(
            ProcessName: "",
            Details: "Watching Crunchyroll",
            State: "Watching Crunchyroll",
            LargeImageKey: "crunchyroll",
            LargeImageText: "Crunchyroll")
        {
            Name = "Crunchyroll",
            ActivityName = "Crunchyroll",
            ProcessNames = ["firefox.exe", "chrome.exe"],
            WindowTitleContains = ["Crunchyroll"],
            Priority = 100
        },
        new RpcProfile(
            ProcessName: "",
            Details: "Listening to YouTube Music",
            State: "Listening to YouTube Music",
            LargeImageKey: "youtube_music",
            LargeImageText: "YouTube Music")
        {
            Name = "YouTube Music",
            ActivityName = "YouTube Music",
            ProcessNames = ["firefox.exe", "chrome.exe"],
            WindowTitleContains = ["YouTube Music"],
            Priority = 100
        }
    ];
}
