using Microsoft.Win32;
using System.IO;

namespace AutoRP.Services;

public sealed class RegistryStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ApplicationName = "AutoRP";

    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ApplicationName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    public void SetRegistered(bool registered)
    {
        try
        {
            if (registered)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                var executablePath = Environment.ProcessPath;
                if (key is null || string.IsNullOrWhiteSpace(executablePath))
                {
                    throw new InvalidOperationException("AutoRP executable path is unavailable.");
                }

                key.SetValue(ApplicationName, $"\"{executablePath}\"");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ApplicationName, throwOnMissingValue: false);
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException("Windows startup registration access was denied.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Windows startup registration is unavailable.", exception);
        }
    }
}
