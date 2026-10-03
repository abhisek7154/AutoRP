namespace AutoRP.Services;

public interface IStartupService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public interface IStartupRegistration
{
    bool IsRegistered { get; }
    void SetRegistered(bool registered);
}
