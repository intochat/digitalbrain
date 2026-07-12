using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Mcp;

public interface IExternalAuthorizationProbe
{
    Task<ExternalAuthorizationResolution> ResolveAsync(
        CommandEnvelope command,
        ExternalAuthorizationContinuation authorization,
        CancellationToken cancellationToken = default);
}

public sealed class OrleansExternalAuthorizationProbe(IClusterClient cluster) : IExternalAuthorizationProbe
{
    public Task<ExternalAuthorizationResolution> ResolveAsync(
        CommandEnvelope command,
        ExternalAuthorizationContinuation authorization,
        CancellationToken cancellationToken = default)
    {
        var owner = RequestScope.Id(command.Context);
        return authorization.Provider switch
        {
            "google" => cluster.GetGrain<IGmailReadToolGrain>(owner)
                .ResolveAuthorizationAsync(cancellationToken),
            "salesforce" => cluster.GetGrain<ISalesforceReadToolGrain>(owner)
                .ResolveAuthorizationAsync(cancellationToken),
            _ => Task.FromResult(new ExternalAuthorizationResolution(
                ExternalAuthorizationResolutionState.Failed,
                "authorization-provider-unsupported"))
        };
    }
}

/// <summary>Reconciles durable authorization waits without coupling OAuth callbacks to the MCP process.</summary>
public sealed class ExternalAuthorizationWorker(
    ApplicationService application,
    IExternalAuthorizationProbe probe,
    ILogger<ExternalAuthorizationWorker> logger) : BackgroundService
{
    private const int MaximumConcurrentProbes = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var waits = application.GetAwaitingExternalAuthorizations();
            foreach (var wait in waits.Where(wait => wait.Continuation.ExpiresAt <= now))
            {
                application.TryRequeueExternalAuthorization(
                    wait.OperationId,
                    wait.Continuation.AttemptId,
                    new ExternalAuthorizationResolution(
                        ExternalAuthorizationResolutionState.Failed,
                        "external-authorization-expired"));
            }

            var groups = waits
                .Where(wait => wait.Continuation.ExpiresAt > now)
                .GroupBy(wait => (
                    Owner: RequestScope.Id(wait.Command.Context),
                    wait.Continuation.Provider))
                .ToArray();
            for (var offset = 0; offset < groups.Length; offset += MaximumConcurrentProbes)
            {
                var batch = groups
                    .Skip(offset)
                    .Take(MaximumConcurrentProbes)
                    .Select(group => ReconcileAsync(group.ToArray(), stoppingToken));
                try
                {
                    await Task.WhenAll(batch).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileAsync(
        IReadOnlyList<ExternalAuthorizationWait> waits,
        CancellationToken stoppingToken)
    {
        var representative = waits[0];
        using var activity = InoTelemetry.Source.StartActivity("ino.authorization.reconcile");
        activity?.SetTag("db.ino.authorization_provider", representative.Continuation.Provider);
        activity?.SetTag("db.ino.authorization_wait_count", waits.Count);
        try
        {
            var resolution = await ProbeAsync(representative, stoppingToken).ConfigureAwait(false);
            activity?.SetTag(
                "db.ino.authorization_outcome",
                resolution.State.ToString().ToLowerInvariant());
            if (resolution.State == ExternalAuthorizationResolutionState.Waiting) return;
            foreach (var wait in waits)
                application.TryRequeueExternalAuthorization(
                    wait.OperationId,
                    wait.Continuation.AttemptId,
                    resolution);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetTag("db.ino.authorization_outcome", "probe-failed");
            logger.LogWarning(
                "External authorization readiness probe failed with {ExceptionType}.",
                exception.GetType().Name);
        }
    }

    private async Task<ExternalAuthorizationResolution> ProbeAsync(
        ExternalAuthorizationWait wait,
        CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await probe.ResolveAsync(wait.Command, wait.Continuation, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return new(ExternalAuthorizationResolutionState.Waiting);
        }
    }
}
