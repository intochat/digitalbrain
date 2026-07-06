namespace DigitalBrain.Core;

[Alias("DigitalBrain.Core.INeuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias("FireAsync")]
    ValueTask FireAsync<T>(T payload) where T : Synapse;
    [Alias("GetTimelineAsync")]
    Task<IReadOnlyList<Synapse>> GetTimelineAsync();
    [Alias("DeliverAsync")]
    Task DeliverAsync(Synapse synapse);

    // Dual journal accessors (outgoing is primary causal log of actions taken by this neuron).
    [Alias("GetIncomingTimelineAsync")]
    Task<IReadOnlyList<Synapse>> GetIncomingTimelineAsync();
    [Alias("GetOutgoingTimelineAsync")]
    Task<IReadOnlyList<Synapse>> GetOutgoingTimelineAsync();

    // Causal query APIs for lineage traversal without reimplementing in callers (UI, debug, MCP).
    [Alias("GetCausalLineageAsync")]
    Task<IReadOnlyList<Synapse>> GetCausalLineageAsync(string correlationId);
    [Alias("GetTimelineForCorrelationAsync")]
    Task<IReadOnlyList<Synapse>> GetTimelineForCorrelationAsync(string correlationId);

    // Simulation and time-travel primitives (checkpoint, branch, restore).
    [Alias("CreateCheckpointAsync")]
    ValueTask<Checkpoint> CreateCheckpointAsync();
    [Alias("BranchAsync")]
    Task<NeuronId> BranchAsync(Checkpoint checkpoint);
    [Alias("RestoreCheckpointAsync")]
    Task RestoreCheckpointAsync(Checkpoint checkpoint);

    // Orleans diagnostic: which activation host (silo) currently owns this grain. Used in cross-activation tests.
    [Alias("GetSiloIdentityAsync")]
    Task<string> GetSiloIdentityAsync();
}
