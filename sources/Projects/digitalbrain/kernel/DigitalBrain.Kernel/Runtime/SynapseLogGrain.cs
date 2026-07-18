using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Runtime;

// E-RUN #37. The durable broadcast tape — every signal the Cortex fans out is
// appended here before subscribers run, mirroring BrainCatalogGrain's
// IDurableList<Synapse> activity log. Using IDurableList (journaling-backed)
// instead of the proto's IPersistentState<T>+snapshot pattern keeps the silo
// boot path uniform — production wires VolatileStateMachineStorageProvider
// (in-memory dev) or the journaled provider (prod) via AddStateMachineStorage;
// either way this grain participates without per-grain storage configuration.
internal sealed class SynapseLogGrain(
    [FromKeyedServices("entries")] IDurableList<SynapseEnvelope> entries)
    : DurableGrain, ISynapseLogGrain
{
    public async Task AppendAsync(SynapseEnvelope envelope)
    {
        entries.Add(envelope);
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<SynapseEnvelope>> AllAsync()
    {
        IReadOnlyList<SynapseEnvelope> snapshot = entries.ToArray();
        return Task.FromResult(snapshot);
    }
}
