namespace Ino.Kernel;

public interface IDomainRestartService
{
    Task<RestartOutcome> RestartDomainsAsync(TimeSpan timeout, CancellationToken ct = default);
}

public enum RestartOutcome
{
    Restarted,
    PendingRestart,
}
