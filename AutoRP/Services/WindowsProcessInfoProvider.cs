using AutoRP.Models;
using System.Diagnostics;

namespace AutoRP.Services;

public sealed class WindowsProcessInfoProvider : IProcessInfoProvider
{
    public ActiveApplication? GetApplication(uint processId, string windowTitle)
    {
        using var process = Process.GetProcessById((int)processId);
        var executableName = process.ProcessName + ".exe";

        try
        {
            executableName = process.MainModule?.ModuleName ?? executableName;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Some protected processes do not allow module inspection.
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the process information available even when module access is denied.
        }

        return new ActiveApplication(
            process.Id,
            process.ProcessName,
            executableName,
            windowTitle);
    }
}
