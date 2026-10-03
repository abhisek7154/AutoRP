using AutoRP.Models;

namespace AutoRP.Services;

public sealed class PresenceProfileManager : IPresenceProfileManager, IDisposable
{
    private readonly IProfileConfigurationService configurationService;
    private readonly object stateLock = new();
    private IReadOnlyList<RpcProfile> profiles = [];
    private IReadOnlyList<RpcProfile> matchingProfiles = [];

    public PresenceProfileManager(IProfileConfigurationService configurationService)
    {
        this.configurationService = configurationService;
        configurationService.ProfilesChanged += OnProfilesChanged;
        RefreshProfiles();
    }

    public event EventHandler? ProfilesChanged;

    public IReadOnlyList<RpcProfile> Profiles
    {
        get
        {
            lock (stateLock)
            {
                return profiles;
            }
        }
    }

    public RpcProfile? FindProfile(ActiveApplication? application)
    {
        if (application is null)
        {
            return null;
        }

        lock (stateLock)
        {
            return matchingProfiles
                .Where(profile => Matches(profile, application))
                .OrderByDescending(profile => profile.Priority)
                .FirstOrDefault();
        }
    }

    public void Dispose()
    {
        configurationService.ProfilesChanged -= OnProfilesChanged;
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        RefreshProfiles();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshProfiles()
    {
        var configuredProfiles = configurationService.Profiles;

        lock (stateLock)
        {
            profiles = configuredProfiles.ToArray();
            matchingProfiles = configuredProfiles.Where(profile => profile.IsEnabled).ToArray();
        }
    }

    private static bool Matches(RpcProfile profile, ActiveApplication application)
    {
        var processNames = GetProcessNames(profile);
        var titleFragments = GetTitleFragments(profile);
        var processMatches = processNames.Count == 0
            || processNames.Contains(Normalize(application.ExecutableName), StringComparer.OrdinalIgnoreCase)
            || processNames.Contains(Normalize(application.ProcessName), StringComparer.OrdinalIgnoreCase);
        var titleMatches = titleFragments.Count == 0
            || titleFragments.All(fragment => application.WindowTitle.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        return (processNames.Count > 0 || titleFragments.Count > 0)
            && processMatches
            && titleMatches;
    }

    private static IReadOnlyList<string> GetProcessNames(RpcProfile profile)
    {
        return (profile.ProcessNames ?? Array.Empty<string>())
            .Append(profile.ProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> GetTitleFragments(RpcProfile profile)
    {
        return (profile.WindowTitleContains ?? Array.Empty<string>())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string value)
    {
        return value.Trim();
    }
}
