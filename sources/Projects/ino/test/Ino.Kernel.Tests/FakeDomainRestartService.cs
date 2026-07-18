namespace Ino.Kernel.Tests;

internal sealed class FakeDomainRestartService : IDomainRestartService
{
    public int CallCount { get; private set; }
    public Exception? NextError { get; set; }
    public RestartOutcome Outcome { get; set; } = RestartOutcome.Restarted;

    public Task<RestartOutcome> RestartDomainsAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        CallCount++;
        if (NextError is not null) throw NextError;
        return Task.FromResult(Outcome);
    }
}
