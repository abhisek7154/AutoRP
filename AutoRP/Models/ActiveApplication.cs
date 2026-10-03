namespace AutoRP.Models;

public sealed record ActiveApplication(
    int ProcessId,
    string ProcessName,
    string ExecutableName,
    string WindowTitle);
