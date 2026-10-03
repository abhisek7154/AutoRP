namespace AutoRP.Services;

internal sealed class ApplicationShutdownCoordinator(Func<Task> stopServices, Action shutDownApplication)
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
            finally
            {
                shutDownApplication();
            }

            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }
}
