using AutoRP.Models;

namespace AutoRP.Services;

public interface ISettingsService
{
    AutoRpSettings Current { get; }
    event EventHandler? SettingsChanged;
    void Update(AutoRpSettings settings);
}
