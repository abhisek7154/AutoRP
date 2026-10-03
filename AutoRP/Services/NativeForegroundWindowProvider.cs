using System.Runtime.InteropServices;
using System.Text;

namespace AutoRP.Services;

public sealed class NativeForegroundWindowProvider : IForegroundWindowProvider
{
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder text, int maxLength);

    public nint GetForegroundWindow()
    {
        return NativeGetForegroundWindow();
    }

    public uint GetProcessId(nint windowHandle)
    {
        GetWindowThreadProcessId(windowHandle, out var processId);
        return processId;
    }

    public string GetWindowTitle(nint windowHandle)
    {
        var title = new StringBuilder(512);
        GetWindowText(windowHandle, title, title.Capacity);
        return title.ToString();
    }
}
