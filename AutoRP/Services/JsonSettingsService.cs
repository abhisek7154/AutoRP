using AutoRP.Models;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace AutoRP.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string storagePath;
    private readonly JsonSerializerOptions serializerOptions = new() { WriteIndented = true };

    public JsonSettingsService(
        ILogger<JsonSettingsService> logger,
        string? storagePath = null)
    {
        this.storagePath = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoRP",
            "settings.json");
        Current = Load(logger);
    }

    public AutoRpSettings Current { get; private set; }

    public void Update(AutoRpSettings settings)
    {
        var directory = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, serializerOptions));
            File.Move(temporaryPath, storagePath, true);
            Current = settings;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private AutoRpSettings Load(ILogger logger)
    {
        try
        {
            if (File.Exists(storagePath))
            {
                return JsonSerializer.Deserialize<AutoRpSettings>(File.ReadAllText(storagePath), serializerOptions)
                    ?? new AutoRpSettings();
            }
        }
        catch (JsonException exception)
        {
            BackupInvalidSettings(logger);
            logger.LogWarning(exception, "AutoRP settings are invalid; using defaults.");
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "AutoRP settings could not be read; using defaults.");
        }

        return new AutoRpSettings();
    }

    private void BackupInvalidSettings(ILogger logger)
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
            logger.LogDebug(exception, "Could not preserve invalid AutoRP settings.");
        }
    }
}
