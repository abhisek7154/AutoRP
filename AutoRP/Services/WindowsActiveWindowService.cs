using AutoRP.Configuration;
using AutoRP.Models;
using Microsoft.Extensions.Logging;

namespace AutoRP.Services;

public sealed class WindowsActiveWindowService : IActiveWindowService, IDisposable
{
    private readonly ILogger<WindowsActiveWindowService> logger;
    private readonly TimeSpan pollingInterval;
    private readonly IForegroundWindowProvider foregroundWindowProvider;
    private readonly IProcessInfoProvider processInfoProvider;
    private readonly object stateLock = new();
    private CancellationTokenSource? pollingCancellation;
    private Task? pollingTask;
    private Task? pollingStopTask;
    private ActiveApplication? currentApplication;

    public WindowsActiveWindowService(
        AutoRpOptions options,
        ILogger<WindowsActiveWindowService> logger,
        IForegroundWindowProvider? foregroundWindowProvider = null,
        IProcessInfoProvider? processInfoProvider = null)
    {
        pollingInterval = options.PollingInterval <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(2)
            : options.PollingInterval;
        this.logger = logger;
        this.foregroundWindowProvider = foregroundWindowProvider ?? new NativeForegroundWindowProvider();
        this.processInfoProvider = processInfoProvider ?? new WindowsProcessInfoProvider();
    }

    public event EventHandler<ActiveApplicationChangedEventArgs>? ApplicationChanged;

    public ActiveApplication? CurrentApplication
    {
        get
        {
            lock (stateLock)
            {
                return currentApplication;
            }
        }
    }
    public ActiveApplication? GetActiveApplication()
    {
        var windowHandle = foregroundWindowProvider.GetForegroundWindow();
        if (windowHandle == nint.Zero)
        {
            return null;
        }

        var processId = foregroundWindowProvider.GetProcessId(windowHandle);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            var application = processInfoProvider.GetApplication(processId, foregroundWindowProvider.GetWindowTitle(windowHandle));
            if (application is null || application.ProcessId == Environment.ProcessId)
            {
                return null;
            }

            return application;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "The foreground process ended or could not be inspected.");
            return null;
        }
    }

    public void Start()
    {
        lock (stateLock)
        {
            if (pollingTask is not null)
            {
                return;
            }

            pollingCancellation = new CancellationTokenSource();
            pollingTask = PollAsync(pollingCancellation.Token);
            pollingStopTask = null;
        }

        logger.LogInformation("Foreground application detection started with a {PollingInterval} interval.", pollingInterval);
        PublishApplication(GetActiveApplication());
    }

    public void Stop()
    {
        _ = StopAsync();
    }

    public Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (stateLock)
        {
            if (pollingStopTask is not null)
            {
                return pollingStopTask;
            }

            cancellation = pollingCancellation;
            task = pollingTask;
            if (cancellation is null)
            {
                return Task.CompletedTask;
            }

            cancellation.Cancel();
            pollingStopTask = Task.Run(() => FinishStopAsync(task, cancellation));
            return pollingStopTask;
        }
    }

    private async Task FinishStopAsync(Task? task, CancellationTokenSource cancellation)
    {
        try
        {
            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }
        finally
        {
            cancellation.Dispose();
            lock (stateLock)
            {
                if (ReferenceEquals(pollingCancellation, cancellation))
                {
                    pollingCancellation = null;
                    pollingTask = null;
                    pollingStopTask = null;
                }
            }
        }

        logger.LogInformation("Foreground application detection stopped.");
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(pollingInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                PublishApplication(GetActiveApplication());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Foreground application detection stopped unexpectedly.");
        }
    }

    private void PublishApplication(ActiveApplication? application)
    {
        ActiveApplication? previous;
        lock (stateLock)
        {
            previous = currentApplication;
            if (previous == application)
            {
                return;
            }

            currentApplication = application;
        }

        logger.LogInformation(
            application is null
                ? "No supported foreground application is active."
                : "Foreground application changed to {ProcessName} ({ProcessId}).",
            application?.ProcessName,
            application?.ProcessId);

        try
        {
            ApplicationChanged?.Invoke(this, new ActiveApplicationChangedEventArgs(previous, application));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "A foreground application change subscriber failed.");
        }
    }
}
