using AutoRP.Models;

namespace AutoRP.Services;

public interface ISettingsService
{
    AutoRpSettings Current { get; }
    void Update(AutoRpSettings settings);
}
