using AutoRP.Models;

namespace AutoRP.ViewModels;

public sealed class ProfileEditorViewModel
{
    public ProfileEditorViewModel(RpcProfile? profile = null)
    {
        if (profile is null)
        {
            return;
        }

        OriginalName = profile.Name;
        Name = profile.Name;
        ProcessName = profile.ProcessName;
        ProcessNamesText = string.Join(Environment.NewLine, profile.ProcessNames ?? [profile.ProcessName]);
        WindowTitleContainsText = string.Join(Environment.NewLine, profile.WindowTitleContains ?? []);
        Priority = profile.Priority;
        Details = profile.Details;
        State = profile.State;
        LargeImageKey = profile.LargeImageKey ?? string.Empty;
        LargeImageText = profile.LargeImageText ?? string.Empty;
        SmallImageKey = profile.SmallImageKey ?? string.Empty;
        SmallImageText = profile.SmallImageText ?? string.Empty;
        ButtonLabel = profile.Buttons?.FirstOrDefault()?.Label ?? string.Empty;
        ButtonUrl = profile.Buttons?.FirstOrDefault()?.Url ?? string.Empty;
        IsEnabled = profile.IsEnabled;
    }

    public string OriginalName { get; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string ProcessNamesText { get; set; } = string.Empty;
    public string WindowTitleContainsText { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Details { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string LargeImageKey { get; set; } = string.Empty;
    public string LargeImageText { get; set; } = string.Empty;
    public string SmallImageKey { get; set; } = string.Empty;
    public string SmallImageText { get; set; } = string.Empty;
    public string ButtonLabel { get; set; } = string.Empty;
    public string ButtonUrl { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public RpcProfile CreateProfile()
    {
        if (string.IsNullOrWhiteSpace(Name)
            || string.IsNullOrWhiteSpace(Details)
            || string.IsNullOrWhiteSpace(State))
        {
            throw new ArgumentException("Name, details, and state are required.");
        }

        var processNames = ParseList(ProcessNamesText);
        if (processNames.Count == 0 && !string.IsNullOrWhiteSpace(ProcessName))
        {
            processNames = ParseList(ProcessName);
        }

        var titleFragments = ParseList(WindowTitleContainsText);
        if (processNames.Count == 0 && titleFragments.Count == 0)
        {
            throw new ArgumentException("At least one process name or window-title fragment is required.");
        }

        IReadOnlyList<RpcButton>? buttons = null;
        if (!string.IsNullOrWhiteSpace(ButtonLabel) || !string.IsNullOrWhiteSpace(ButtonUrl))
        {
            if (string.IsNullOrWhiteSpace(ButtonLabel) || string.IsNullOrWhiteSpace(ButtonUrl))
            {
                throw new ArgumentException("Both button label and URL are required when adding a button.");
            }

            buttons = [new RpcButton(ButtonLabel.Trim(), ButtonUrl.Trim())];
        }

        return new RpcProfile(
            processNames.FirstOrDefault() ?? string.Empty,
            Details.Trim(),
            State.Trim(),
            EmptyToNull(LargeImageKey),
            EmptyToNull(LargeImageText),
            EmptyToNull(SmallImageKey),
            EmptyToNull(SmallImageText),
            buttons)
        {
            Name = Name.Trim(),
            IsEnabled = IsEnabled,
            Priority = Priority,
            ProcessNames = processNames,
            WindowTitleContains = titleFragments
        };
    }

    private static List<string> ParseList(string value)
    {
        return value
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
