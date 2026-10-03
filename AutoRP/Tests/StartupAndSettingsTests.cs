using AutoRP.Models;
using AutoRP.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AutoRP.Tests;

public sealed class StartupAndSettingsTests
{
    [Fact]
    public void SettingsPersistStartupAndCloseToTrayPreferences()
    {
        using var context = TestContext.Create();
        context.Settings.Update(new AutoRpSettings(StartWithWindows: true, CloseToTray: false));

        using var reloaded = new TestContext(context.Path);
        Assert.True(reloaded.Settings.Current.StartWithWindows);
        Assert.False(reloaded.Settings.Current.CloseToTray);
    }

    [Fact]
    public void EnablingStartupRegistersOnceAndDisablingRemovesRegistration()
    {
        using var context = TestContext.Create();
        var service = new StartupService(context.Settings, context.Registration, context.LoggerFactory.CreateLogger<StartupService>());

        service.SetEnabled(true);
        service.SetEnabled(true);
        Assert.True(service.IsEnabled);
        Assert.Equal(1, context.Registration.RegisterCalls);

        service.SetEnabled(false);
        Assert.False(service.IsEnabled);
        Assert.Equal(1, context.Registration.UnregisterCalls);
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(string path)
        {
            Path = path;
            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { });
            Settings = new JsonSettingsService(LoggerFactory.CreateLogger<JsonSettingsService>(), path);
            Registration = new FakeRegistration();
        }

        public static TestContext Create()
        {
            return new TestContext(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"autor p-settings-{Guid.NewGuid():N}.json"));
        }

        public string Path { get; }
        public ILoggerFactory LoggerFactory { get; }
        public JsonSettingsService Settings { get; }
        public FakeRegistration Registration { get; }

        public void Dispose()
        {
            LoggerFactory.Dispose();
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }

    private sealed class FakeRegistration : IStartupRegistration
    {
        public bool IsRegistered { get; private set; }
        public int RegisterCalls { get; private set; }
        public int UnregisterCalls { get; private set; }

        public void SetRegistered(bool registered)
        {
            if (registered)
            {
                RegisterCalls++;
            }
            else
            {
                UnregisterCalls++;
            }

            IsRegistered = registered;
        }
    }
}
