namespace DigitalBrain.Runtime;

// Cluster-wide singleton (keyed by Guid.Empty) that owns the catalog of dynamic
// neurons. Slice-2 Creator stages new specs here, runs the .feature against the
// staged DynamicNeuronGrain, and promotes on green. Lookups during gateway
// dispatch consult Promoted entries when no static neuron handles a synapse type.
[Orleans.Metadata.DefaultGrainType("NeuronRegistryGrain")]
public interface INeuronRegistry : IGrainWithGuidKey
{
    Task StageAsync(DynamicNeuronSpec spec);
    Task PromoteAsync(NeuronId id);
    Task RetireAsync(NeuronId id);
    Task<DynamicNeuronSpec?> GetAsync(NeuronId id);
    Task<IReadOnlyList<DynamicNeuronSpec>> ListAsync(DynamicNeuronStatus? status = null);
}
