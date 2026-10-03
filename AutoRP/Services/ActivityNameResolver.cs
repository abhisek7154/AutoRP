using AutoRP.Models;
using System.IO;

namespace AutoRP.Services;

internal static class ActivityNameResolver
{
    public static string Resolve(ActiveApplication? application, RpcProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ActivityName))
        {
            return profile.ActivityName.Trim();
        }

        if (application is not null)
        {
            var executableName = Path.GetFileNameWithoutExtension(application.ExecutableName);
            var processName = string.IsNullOrWhiteSpace(executableName)
                ? application.ProcessName
                : executableName;

            return processName.ToLowerInvariant() switch
            {
                "firefox" => "Firefox",
                "chrome" => "Google Chrome",
                "code" => "Visual Studio Code",
                "spotify" => "Spotify",
                _ => Normalize(processName)
            };
        }

        var profileProcess = Path.GetFileNameWithoutExtension(profile.ProcessName);
        return !string.IsNullOrWhiteSpace(profileProcess)
            ? profileProcess
            : profile.Name.Trim();
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "AutoRP" : value.Trim();
    }
}
