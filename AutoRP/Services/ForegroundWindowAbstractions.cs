using AutoRP.Models;

namespace AutoRP.Services;

public interface IForegroundWindowProvider
{
    nint GetForegroundWindow();
    uint GetProcessId(nint windowHandle);
    string GetWindowTitle(nint windowHandle);
}

public interface IProcessInfoProvider
{
    ActiveApplication? GetApplication(uint processId, string windowTitle);
}
