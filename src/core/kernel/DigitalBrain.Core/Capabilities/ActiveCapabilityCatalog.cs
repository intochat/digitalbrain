using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public sealed class ActiveCapabilityCatalog
{
    private readonly IReadOnlyDictionary<string, CapabilityManifest> _modules;
    private readonly IReadOnlyDictionary<string, NeuronCapabilityDescriptor> _neurons;
    private readonly IReadOnlyDictionary<(string ContractId, int SchemaVersion), SynapseCapabilityDescriptor> _synapses;

    private ActiveCapabilityCatalog(
        IReadOnlyList<CapabilityManifest> modules,
        IReadOnlyDictionary<string, CapabilityManifest> moduleIndex,
        IReadOnlyDictionary<string, NeuronCapabilityDescriptor> neurons,
        IReadOnlyDictionary<(string ContractId, int SchemaVersion), SynapseCapabilityDescriptor> synapses)
    {
        Modules = modules;
        _modules = moduleIndex;
        _neurons = neurons;
        _synapses = synapses;
    }

    public IReadOnlyList<CapabilityManifest> Modules { get; }

    public static ActiveCapabilityCatalog Create(IEnumerable<ICompiledModule> selectedModules)
    {
        var manifests = selectedModules
            .Select(module => module.Capabilities)
            .OrderBy(manifest => manifest.ModuleId.Value, StringComparer.Ordinal)
            .ToArray();

        var moduleIndex = new Dictionary<string, CapabilityManifest>(StringComparer.Ordinal);
        var neurons = new Dictionary<string, NeuronCapabilityDescriptor>(StringComparer.Ordinal);
        var neuronOwners = new Dictionary<string, ModuleId>(StringComparer.Ordinal);
        var synapses = new Dictionary<(string, int), SynapseCapabilityDescriptor>();

        foreach (var manifest in manifests)
        {
            if (!moduleIndex.TryAdd(manifest.ModuleId.Value, manifest))
            {
                throw new InvalidOperationException(
                    $"Duplicate active module capability id '{manifest.ModuleId.Value}'.");
            }

            foreach (var neuron in manifest.Neurons)
            {
                if (!neurons.TryAdd(neuron.ContractId, neuron))
                {
                    var prior = neuronOwners[neuron.ContractId];
                    throw new InvalidOperationException(
                        $"Duplicate active neuron capability id '{neuron.ContractId}' "
                        + $"from modules '{prior.Value}' and '{manifest.ModuleId.Value}'.");
                }

                neuronOwners[neuron.ContractId] = manifest.ModuleId;
                IndexSynapses(synapses, neuron.Accepted, neuron.ContractId);
                IndexSynapses(synapses, neuron.Emitted, neuron.ContractId);
            }
        }

        return new ActiveCapabilityCatalog(manifests, moduleIndex, neurons, synapses);
    }

    public bool TryGetModule(ModuleId moduleId, out CapabilityManifest? manifest)
        => _modules.TryGetValue(moduleId.Value, out manifest);

    public bool TryGetNeuron(string contractId, out NeuronCapabilityDescriptor? neuron)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        return _neurons.TryGetValue(contractId, out neuron);
    }

    public bool TryGetSynapse(string contractId, int schemaVersion, out SynapseCapabilityDescriptor? synapse)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        return _synapses.TryGetValue((contractId, schemaVersion), out synapse);
    }

    private static void IndexSynapses(
        Dictionary<(string, int), SynapseCapabilityDescriptor> synapses,
        IReadOnlyList<SynapseCapabilityDescriptor> descriptors,
        string neuronContractId)
    {
        foreach (var synapse in descriptors)
        {
            var key = (synapse.ContractId, synapse.SchemaVersion);
            if (synapses.TryGetValue(key, out var existing)
                && !string.Equals(existing.JsonSchema, synapse.JsonSchema, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Incompatible active schemas for synapse '{synapse.ContractId}' v{synapse.SchemaVersion} "
                    + $"(seen via neuron '{neuronContractId}').");
            }

            if (!synapses.TryAdd(key, synapse)
                && !string.Equals(synapses[key].JsonSchema, synapse.JsonSchema, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Duplicate incompatible synapse capability '{synapse.ContractId}' v{synapse.SchemaVersion}.");
            }
        }
    }
}
