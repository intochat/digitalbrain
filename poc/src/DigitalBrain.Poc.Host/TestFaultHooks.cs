using System.Diagnostics;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Host;

internal static class TestFaultHooks
{
    private static int _chartCommitFaultFired;
    private static int _generatedLocalFaultFired;

    public static CandidateAssemblyLoader CreateCandidateLoader(bool enabled)
    {
        if (!enabled ||
            !Enum.TryParse<HostFault>(
                Environment.GetEnvironmentVariable(ActiveHostBootstrap.TestFaultEnvironment),
                out var fault))
        {
            return new CandidateAssemblyLoader();
        }

        return fault switch
        {
            HostFault.AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement =>
                CreateGeneratedLocalFaultLoader(),
            HostFault.AfterTrustedFanOutCommitBeforeRuleAcknowledgement =>
                new CandidateAssemblyLoader(null, CrashAfterCandidateDelivery),
            _ => new CandidateAssemblyLoader(),
        };
    }

    public static Func<CancellationToken, Task>? CreateAfterChartCommit(bool enabled)
    {
        if (!enabled ||
            !Enum.TryParse<HostFault>(
                Environment.GetEnvironmentVariable(ActiveHostBootstrap.TestFaultEnvironment),
                out var fault) ||
            fault != HostFault.AfterChartNeuronCommitBeforeUpstreamOutboxAcknowledgement)
        {
            return null;
        }

        Interlocked.Exchange(ref _chartCommitFaultFired, 0);
        return CrashAfterChartCommit;
    }

    private static CandidateAssemblyLoader CreateGeneratedLocalFaultLoader()
    {
        Interlocked.Exchange(ref _generatedLocalFaultFired, 0);
        return new CandidateAssemblyLoader(CrashAfterGeneratedLocalOutboxCommit, null);
    }

    private static Task CrashAfterGeneratedLocalOutboxCommit(
        SynapseEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.TargetRevision is null ||
            !string.Equals(
                envelope.Synapse.GetType().Name,
                "ElonPostMatched",
                StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _generatedLocalFaultFired, 1) == 0)
        {
            Environment.FailFast(
                "Test-only crash after generated local outbox commit and before forwarding.");
            throw new UnreachableException();
        }

        return Task.CompletedTask;
    }

    private static Task CrashAfterCandidateDelivery(
        PendingOutboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        _ = envelope;
        cancellationToken.ThrowIfCancellationRequested();
        Environment.FailFast(
            "Test-only crash after trusted fan-out commit and before local acknowledgement.");
        throw new UnreachableException();
    }

    private static Task CrashAfterChartCommit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _chartCommitFaultFired, 1) == 0)
        {
            Environment.FailFast(
                "Test-only crash after ChartNeuron commit and before upstream outbox acknowledgement.");
        }

        return Task.CompletedTask;
    }
}
