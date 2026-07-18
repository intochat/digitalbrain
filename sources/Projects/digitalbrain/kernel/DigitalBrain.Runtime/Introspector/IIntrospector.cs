using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

public interface IIntrospector : INeuron
{
    Task<IReadOnlyList<NeuronRef>> FindNeuronsByFeatureTextAsync(string query, int limit, CancellationToken ct);
    Task<IReadOnlyList<Guid>> FindChainsByConversationTextAsync(string text, DateTimeOffset? since, DateTimeOffset? until, int limit, CancellationToken ct);
    Task<IReadOnlyList<Synapse>> TraceCorrelationAsync(Guid correlationId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetRecentActivityAsync(string userId, TimeSpan since, CancellationToken ct);
    Task<Synapse?> FindRootSynapseAsync(Guid synapseId, CancellationToken ct);
}
