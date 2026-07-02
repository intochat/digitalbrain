namespace DigitalBrain.Core;

public interface INeuron : IGrainWithStringKey
{
    ValueTask FireAsync<T>(T payload) where T : Synapse;
    Task<IReadOnlyList<Synapse>> GetTimelineAsync();
    Task DeliverAsync(Synapse synapse);

    // Dual journal accessors (outgoing is primary causal log of actions taken by this neuron).
    Task<IReadOnlyList<Synapse>> GetIncomingTimelineAsync();
    Task<IReadOnlyList<Synapse>> GetOutgoingTimelineAsync();

    // Causal query APIs for lineage traversal without reimplementing in callers (UI, debug, MCP).
    Task<IReadOnlyList<Synapse>> GetCausalLineageAsync(string correlationId);
    Task<IReadOnlyList<Synapse>> GetTimelineForCorrelationAsync(string correlationId);

    // Simulation and time-travel primitives (checkpoint, branch, restore).
    ValueTask<Checkpoint> CreateCheckpointAsync();
    Task<NeuronId> BranchAsync(Checkpoint checkpoint);
    Task RestoreCheckpointAsync(Checkpoint checkpoint);

    // Identifies which silo currently hosts this activation, so callers (tests, ops tooling) can prove
    // cross-silo placement/delivery instead of assuming it.
    Task<string> GetSiloIdentityAsync();
}
