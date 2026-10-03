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
            LargeImageText: "Visual Studio Code")
        {
            Name = "Visual Studio Code",
            ProcessNames = ["Code.exe"],
            Priority = 10
        },
        new RpcProfile(
            ProcessName: "chrome.exe",
            Details: "Browsing with Chrome",
            State: "Researching AutoRP",
            LargeImageText: "Google Chrome")
        {
            Name = "Google Chrome",
            ProcessNames = ["chrome.exe"],
            Priority = 10
        },
        new RpcProfile(
            ProcessName: "firefox.exe",
            Details: "Browsing with Firefox",
            State: "Researching AutoRP",
            LargeImageText: "Mozilla Firefox")
        {
            Name = "Mozilla Firefox",
            ProcessNames = ["firefox.exe"],
            Priority = 20
        },
        new RpcProfile(
            ProcessName: "Spotify.exe",
            Details: "Listening on Spotify",
            State: "Enjoying music",
            LargeImageText: "Spotify")
        {
            Name = "Spotify",
            ProcessNames = ["Spotify.exe"],
            Priority = 10
        }
    ];
}
