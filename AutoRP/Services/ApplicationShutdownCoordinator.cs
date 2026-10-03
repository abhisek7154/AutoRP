using Microsoft.Extensions.Logging;

namespace AutoRP.Services;

internal sealed class ApplicationShutdownCoordinator(
    Func<Task> stopServices,
    Action shutDownApplication,
    ILogger? logger = null)
{
    private readonly object sync = new();
    private Task? shutdownTask;

    public Task ShutdownAsync()
    {
        TaskCompletionSource completion;
        lock (sync)
        {
            if (shutdownTask is not null)
            {
                return shutdownTask;
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdownTask = completion.Task;
        }

        logger?.LogInformation("Shutdown requested.");
        _ = RunShutdownAsync(completion);
        return completion.Task;
    }

    private async Task RunShutdownAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                await stopServices();
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "One or more services failed during shutdown cleanup.");
            }
            finally
            {
                logger?.LogInformation("Calling Application shutdown.");
                shutDownApplication();
            }

            completion.TrySetResult();
            logger?.LogInformation("Shutdown completed.");
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }
}
