using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang.Planning;
using DigitalBrain.InoLang.Runtime;

namespace DigitalBrain.Kernel.Runtime;

// Pure dispatch logic — shared by the production grain and the test
// TestableInterpretedNeuron so the test exercises the real code path and
// the two implementations cannot drift (mirrors the ConversationQueries
// helper-extraction pattern used by ConversationGrainTests).
internal static class InterpretedNeuronDispatcher
{
    public static async Task<(ScenarioGateResult Gate, ExecutionPlan? Plan)> ConfigureAsync(
        IPlanCache plans, NeuronDescriptor descriptor, CancellationToken ct)
    {
        var entry = await plans.GetOrCompileAsync(descriptor, ct);
        return (entry.Gate, entry.Plan);
    }

    // Returns the Interpreter's ActivationResult so the caller (the grain)
    // can hand it to the silo-resident Cortex for emit fan-out (E-RUN #37).
    // null = dropped envelope (no handler).
    //
    // Dispatch order: SignalFqn handler first, then Synapse port match. The
    // SignalFqn path matches by envelope.TypeFqn directly (no port-name
    // indirection); the Synapse path uses descriptor.Incoming's FQN→PortName
    // map to find the trigger key. Putting SignalFqn first means an authored
    // `using #payload = synapse(Acme.X)` declaration (used purely to expose
    // `#payload.value` field access on a broadcast envelope) does NOT short-
    // circuit the broadcast subscriber handler.
    public static async Task<ActivationResult?> HandleAsync(
        ExecutionPlan? cachedPlan,
        NeuronDescriptor? descriptor,
        SynapseEnvelope envelope,
        IReadOnlyDictionary<string, string>? memory,
        INeuronHost neurons,
        CancellationToken ct,
        Action<string, string, string, double, string, Guid, Guid?>? onTrace = null)
    {
        if (cachedPlan is null || descriptor is null)
            return null;

        var signalKey = TriggerKey.Broadcast(envelope.TypeFqn);
        if (cachedPlan.HandlersFor(signalKey).Count > 0)
        {
            return await new Interpreter(cachedPlan) { OnTrace = onTrace }.RunAsync(
                signalKey, envelope.Payload, neurons, memory, ct);
        }

        var port = descriptor.Incoming.FirstOrDefault(port =>
            string.Equals(port.Fqn, envelope.TypeFqn, StringComparison.Ordinal));
        if (port is null)
            return null;

        return await new Interpreter(cachedPlan) { OnTrace = onTrace }.RunAsync(
            TriggerKey.Port(port.PortName), envelope.Payload, neurons, memory, ct);
    }

    // E-RUN #38. Lifecycle dispatch — `on activated:` / `on deactivated:` /
    // `on created:`. The grain calls this from OnActivateAsync/OnDeactivateAsync
    // with no inbound envelope; the Interpreter sees an empty inbound dict.
    // Returning null when there is no matching handler (or the plan was never
    // seated) lets the caller skip the broadcast step without a noop result.
    public static async Task<ActivationResult?> DispatchLifecycleAsync(
        ExecutionPlan? cachedPlan,
        string lifecycleName,
        IReadOnlyDictionary<string, string>? memory,
        INeuronHost neurons,
        CancellationToken ct,
        Action<string, string, string, double, string, Guid, Guid?>? onTrace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lifecycleName);
        if (cachedPlan is null)
            return null;

        var key = TriggerKey.Lifecycle(lifecycleName);
        if (cachedPlan.HandlersFor(key).Count == 0)
            return null;

        return await new Interpreter(cachedPlan) { OnTrace = onTrace }.RunAsync(
            key, EmptyInbound, neurons, memory, ct);
    }

    static readonly IReadOnlyDictionary<string, string> EmptyInbound =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
