namespace DigitalBrain.Execution;

// Testing/Fakes-only seam: proves Script driver routing by invoking the same allow-listed
// capability handlers as Agent. Production AppHost will start the DigitalBrain.Scripting
// executable later and replace this with an out-of-process IPC adapter — Kernel must never
// load generated C#.
public sealed class InProcessAllowListedScriptDriver(EffectBroker broker) : IScriptDriver
{
    public async Task RunAsync(
        ExecutionSession session,
        WorkloadDescriptor workload,
        IReadOnlyList<CapabilityId> grants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(grants);

        var requestJson = $$"""{"workload":"{{workload.GetType().Name}}"}""";
        for (var i = 0; i < grants.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grant = grants[i];
            if (!broker.IsRegistered(grant))
            {
                continue;
            }

            var delta = await session.CallAsync(grant, requestJson, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await session.ApplyDeltaAsync(delta)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }
}
