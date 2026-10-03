namespace AutoRP.Models;

public sealed record RpcButton(
    string Label,
    string Url);

public sealed record RpcProfile(
    string ProcessName,
    string Details,
    string State,
    string? LargeImageKey = null,
    string? LargeImageText = null,
    string? SmallImageKey = null,
    string? SmallImageText = null,
    IReadOnlyList<RpcButton>? Buttons = null)
{
    public string Name { get; init; } = string.Empty;
    public string? ActivityName { get; init; }
    public bool IsEnabled { get; init; } = true;
    public int Priority { get; init; }
    public IReadOnlyList<string>? ProcessNames { get; init; }
    public IReadOnlyList<string>? WindowTitleContains { get; init; }
}
