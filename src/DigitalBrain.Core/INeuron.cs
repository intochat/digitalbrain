namespace DigitalBrain.Core;

[Alias("DigitalBrain.Core.INeuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias("FireAsync")]
    Task FireAsync<T>(T payload, CancellationToken cancellationToken = default) where T : Synapse;
    [Alias("GetTimelineAsync")]
    Task<IReadOnlyList<Synapse>> GetTimelineAsync(CancellationToken cancellationToken = default);
    [Alias("DeliverAsync")]
    Task DeliverAsync(Synapse synapse, CancellationToken cancellationToken = default);

    // Dual journal accessors (outgoing is primary causal log of actions taken by this neuron).
    [Alias("GetIncomingTimelineAsync")]
    Task<IReadOnlyList<Synapse>> GetIncomingTimelineAsync(CancellationToken cancellationToken = default);
    [Alias("GetOutgoingTimelineAsync")]
    Task<IReadOnlyList<Synapse>> GetOutgoingTimelineAsync(CancellationToken cancellationToken = default);

    // Causal query APIs for lineage traversal without reimplementing in callers (UI, debug, MCP).
    [Alias("GetCausalLineageAsync")]
    Task<IReadOnlyList<Synapse>> GetCausalLineageAsync(string correlationId, CancellationToken cancellationToken = default);
    [Alias("GetTimelineForCorrelationAsync")]
    Task<IReadOnlyList<Synapse>> GetTimelineForCorrelationAsync(string correlationId, CancellationToken cancellationToken = default);

    // Simulation and time-travel primitives (checkpoint, branch, restore).
    [Alias("CreateCheckpointAsync")]
    Task<Checkpoint> CreateCheckpointAsync(CancellationToken cancellationToken = default);
    [Alias("BranchAsync")]
    Task<NeuronId> BranchAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default);
    [Alias("RestoreCheckpointAsync")]
    Task RestoreCheckpointAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default);

    // Orleans diagnostic: which activation host (silo) currently owns this grain. Used in cross-activation tests.
    [Alias("GetSiloIdentityAsync")]
    Task<string> GetSiloIdentityAsync(CancellationToken cancellationToken = default);
}
