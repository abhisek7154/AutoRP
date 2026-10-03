using AutoRP.Configuration;
using AutoRP.Models;
using AutoRP.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AutoRP.Tests;

public sealed class ConfigurationReliabilityTests
{
    [Fact]
    public void LoadsValidProfilesWhenAnotherEntryIsMalformed()
    {
        var path = TemporaryPath();
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<JsonProfileConfigurationService>();
        File.WriteAllText(path, "[{\"Name\":\"Valid\",\"ProcessName\":\"valid.exe\",\"Details\":\"Valid\",\"State\":\"Testing\"},{\"Name\":123,\"ProcessNames\":\"not-an-array\"}]");

        try
        {
            var service = new JsonProfileConfigurationService(new AutoRpOptions { Profiles = [] }, logger, path);

            Assert.Single(service.Profiles);
            Assert.Equal("Valid", service.Profiles[0].Name);
        }
        finally
        {
            DeleteConfigurationFiles(path);
        }
    }

    [Fact]
    public void PreservesMalformedProfileJsonInsteadOfOverwritingIt()
    {
        var path = TemporaryPath();
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<JsonProfileConfigurationService>();
        File.WriteAllText(path, "{ malformed");

        try
        {
            var service = new JsonProfileConfigurationService(
                new AutoRpOptions
                {
                    Profiles = [new RpcProfile("fallback.exe", "Fallback", "Testing")]
                },
                logger,
                path);

            Assert.Single(service.Profiles);
            Assert.NotEmpty(Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".invalid-*.json"));
            Assert.Equal("{ malformed", File.ReadAllText(path));
        }
        finally
        {
            DeleteConfigurationFiles(path);
        }
    }

    [Fact]
    public void PreservesMalformedSettingsJsonInsteadOfOverwritingIt()
    {
        var path = TemporaryPath();
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<JsonSettingsService>();
        File.WriteAllText(path, "{ malformed");

        try
        {
            var service = new JsonSettingsService(logger, path);

            Assert.Equal(new AutoRpSettings(), service.Current);
            Assert.NotEmpty(Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".invalid-*.json"));
            Assert.Equal("{ malformed", File.ReadAllText(path));
        }
        finally
        {
            DeleteConfigurationFiles(path);
        }
    }

    private static string TemporaryPath()
    {
        return Path.Combine(Path.GetTempPath(), $"autor p-reliability-{Guid.NewGuid():N}.json");
    }

    private static void DeleteConfigurationFiles(string path)
    {
        foreach (var file in Directory.GetFiles(
                     Path.GetDirectoryName(path)!,
                     Path.GetFileName(path) + "*"))
        {
            File.Delete(file);
        }
    }
}
