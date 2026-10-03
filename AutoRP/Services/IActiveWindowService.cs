using AutoRP.Models;

namespace AutoRP.Services;

public interface IActiveWindowService
{
    event EventHandler<ActiveApplicationChangedEventArgs>? ApplicationChanged;
    ActiveApplication? CurrentApplication { get; }
    ActiveApplication? GetActiveApplication();
    void Start();
    void Stop();
    Task StopAsync()
    {
        return Task.CompletedTask;
    }
}
