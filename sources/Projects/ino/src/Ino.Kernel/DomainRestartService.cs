using Microsoft.Extensions.Logging;

namespace Ino.Kernel;

public sealed class NullDomainRestartService(ILogger<NullDomainRestartService> logger)
    : IDomainRestartService
{
    public Task<RestartOutcome> RestartDomainsAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        logger.LogWarning("NullDomainRestartService.RestartDomainsAsync called — no-op. " +
            "Wire an IDomainRestartService backed by Aspire's ResourceCommandService to enable real restarts.");
        return Task.FromResult(RestartOutcome.PendingRestart);
    }
}
