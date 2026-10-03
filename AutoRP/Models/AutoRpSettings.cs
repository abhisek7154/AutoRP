namespace AutoRP.Models;

public sealed record AutoRpSettings(
    bool StartWithWindows = false,
    bool CloseToTray = true,
    string DiscordApplicationId = "");
