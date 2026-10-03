using AutoRP.Configuration;
using AutoRP.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace AutoRP.Services;

public sealed class JsonProfileConfigurationService : IProfileConfigurationService
{
    private readonly object stateLock = new();
    private readonly string storagePath;
    private readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };
    private List<RpcProfile> profiles;

    public JsonProfileConfigurationService(
        AutoRpOptions options,
        ILogger<JsonProfileConfigurationService> logger,
        string? storagePath = null)
    {
        this.storagePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoRP",
            "profiles.json");
        profiles = LoadProfiles(options, logger);
    }

    public event EventHandler? ProfilesChanged;

    public IReadOnlyList<RpcProfile> Profiles
    {
        get
        {
            lock (stateLock)
            {
                return profiles.ToArray();
            }
        }
    }

    public RpcProfile AddProfile(RpcProfile profile)
    {
        ValidateProfile(profile);
        lock (stateLock)
        {
            EnsureUniqueName(profile.Name, null);
            var updatedProfiles = profiles.Append(profile).ToList();
            SaveProfiles(updatedProfiles);
            profiles = updatedProfiles;
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return profile;
    }

    public void UpdateProfile(string originalName, RpcProfile profile)
    {
        ValidateProfile(profile);
        lock (stateLock)
        {
            var index = FindIndex(originalName);
            EnsureUniqueName(profile.Name, originalName);
            var updatedProfiles = profiles.ToList();
            updatedProfiles[index] = profile;
            SaveProfiles(updatedProfiles);
            profiles = updatedProfiles;
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool DeleteProfile(string profileName)
    {
        lock (stateLock)
        {
            var index = FindIndexOrDefault(profileName);
            if (index < 0)
            {
                return false;
            }

            var updatedProfiles = profiles.ToList();
            updatedProfiles.RemoveAt(index);
            SaveProfiles(updatedProfiles);
            profiles = updatedProfiles;
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SetProfileEnabled(string profileName, bool enabled)
    {
        lock (stateLock)
        {
            var index = FindIndex(profileName);
            var updatedProfiles = profiles.ToList();
            updatedProfiles[index] = updatedProfiles[index] with { IsEnabled = enabled };
            SaveProfiles(updatedProfiles);
            profiles = updatedProfiles;
        }

        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<RpcProfile> LoadProfiles(AutoRpOptions options, ILogger logger)
    {
        try
        {
            if (File.Exists(storagePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(storagePath));
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("Profile configuration root must be an array.");
                }

                var loaded = new List<RpcProfile>();
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    try
                    {
                        var profile = element.Deserialize<RpcProfile>(serializerOptions);
                        if (profile is not null && HasMatchingCriteria(profile))
                        {
                            loaded.Add(profile);
                        }
                        else
                        {
                            logger.LogWarning("Skipped an invalid profile entry without matching criteria.");
                        }
                    }
                    catch (JsonException exception)
                    {
                        logger.LogWarning(exception, "Skipped an invalid profile entry.");
                    }
                }

                return loaded;
            }
        }
        catch (JsonException exception)
        {
            BackupInvalidConfiguration(logger);
            logger.LogWarning(exception, "Profile configuration is invalid; using sample profiles without overwriting the file.");
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Profile configuration could not be read; using sample profiles.");
        }

        return options.Profiles.ToList();
    }

    private void SaveProfiles(IReadOnlyList<RpcProfile> profilesToSave)
    {
        var directory = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profilesToSave, serializerOptions));
            File.Move(temporaryPath, storagePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void BackupInvalidConfiguration(ILogger logger)
    {
        try
        {
            if (File.Exists(storagePath))
            {
                File.Copy(storagePath, storagePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json");
            }
        }
        catch (IOException exception)
        {
            logger.LogDebug(exception, "Could not preserve the invalid profile configuration.");
        }
    }

    private static bool HasMatchingCriteria(RpcProfile profile)
    {
        return !string.IsNullOrWhiteSpace(profile.ProcessName)
            || (profile.ProcessNames?.Any(name => !string.IsNullOrWhiteSpace(name)) ?? false)
            || (profile.WindowTitleContains?.Any(title => !string.IsNullOrWhiteSpace(title)) ?? false);
    }

    private void ValidateProfile(RpcProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Profile name is required.", nameof(profile));
        }

        var hasProcessCriteria = (profile.ProcessNames ?? Array.Empty<string>()).Any(name => !string.IsNullOrWhiteSpace(name))
            || !string.IsNullOrWhiteSpace(profile.ProcessName);
        var hasTitleCriteria = (profile.WindowTitleContains ?? Array.Empty<string>()).Any(title => !string.IsNullOrWhiteSpace(title));
        if (!hasProcessCriteria && !hasTitleCriteria)
        {
            throw new ArgumentException("At least one process name or window-title fragment is required.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.Details))
        {
            throw new ArgumentException("Details are required.", nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(profile.State))
        {
            throw new ArgumentException("State is required.", nameof(profile));
        }
    }

    private int FindIndex(string profileName)
    {
        var index = FindIndexOrDefault(profileName);
        return index >= 0 ? index : throw new KeyNotFoundException($"Profile '{profileName}' was not found.");
    }

    private int FindIndexOrDefault(string profileName)
    {
        return profiles.FindIndex(profile => string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureUniqueName(string name, string? originalName)
    {
        if (profiles.Any(profile => !string.Equals(profile.Name, originalName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A profile named '{name}' already exists.");
        }
    }
}