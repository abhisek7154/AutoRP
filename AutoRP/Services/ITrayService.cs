namespace AutoRP.Services;

public interface ITrayService : IDisposable
{
    event EventHandler? OpenRequested;
    event EventHandler? ExitRequested;
    bool IsExitRequested { get; }
    void RequestExit();
    void Start();
    void UpdateStatus();
}
