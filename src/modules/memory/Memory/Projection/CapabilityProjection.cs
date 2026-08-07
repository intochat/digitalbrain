using System.Globalization;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Memory;

public static class CapabilityProjection
{
    public static IReadOnlyList<VectorProjectionEntry> FromCatalog(ActiveCapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var entries = new List<VectorProjectionEntry>();
        foreach (var module in catalog.Modules)
        {
            entries.Add(ModuleEntry(module));
            foreach (var neuron in module.Neurons)
            {
                entries.Add(NeuronEntry(module, neuron));
                foreach (var synapse in neuron.Accepted)
                {
                    entries.Add(SynapseEntry(module, neuron, synapse));
                }

                foreach (var synapse in neuron.Emitted)
                {
                    entries.Add(SynapseEntry(module, neuron, synapse));
                }
            }
        }

        return entries
            .GroupBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static VectorProjectionEntry ModuleEntry(CapabilityManifest module)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VectorProjectionMetadataKeys.Kind] = VectorProjectionKinds.Module,
            [VectorProjectionMetadataKeys.ModuleId] = module.ModuleId.Value,
            [VectorProjectionMetadataKeys.ContractId] = module.ModuleId.Value,
        };

        var text = new StringBuilder()
            .Append(module.ModuleId.Value)
            .Append(' ')
            .Append(module.Description)
            .Append(" version ")
            .Append(module.Version)
            .ToString();

        return new VectorProjectionEntry(module.ModuleId.Value, text, metadata);
    }

    private static VectorProjectionEntry NeuronEntry(CapabilityManifest module, NeuronCapabilityDescriptor neuron)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VectorProjectionMetadataKeys.Kind] = VectorProjectionKinds.Neuron,
            [VectorProjectionMetadataKeys.ModuleId] = module.ModuleId.Value,
            [VectorProjectionMetadataKeys.ContractId] = neuron.ContractId,
            [VectorProjectionMetadataKeys.NeuronContractId] = neuron.ContractId,
        };

        var text = new StringBuilder()
            .Append(neuron.ContractId)
            .Append(' ')
            .Append(neuron.Description)
            .Append(" module ")
            .Append(module.ModuleId.Value)
            .ToString();

        return new VectorProjectionEntry(neuron.ContractId, text, metadata);
    }

    private static VectorProjectionEntry SynapseEntry(
        CapabilityManifest module,
        NeuronCapabilityDescriptor neuron,
        SynapseCapabilityDescriptor synapse)
    {
        var key = SynapseKey(synapse.ContractId, synapse.SchemaVersion);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VectorProjectionMetadataKeys.Kind] = VectorProjectionKinds.Synapse,
            [VectorProjectionMetadataKeys.ModuleId] = module.ModuleId.Value,
            [VectorProjectionMetadataKeys.ContractId] = synapse.ContractId,
            [VectorProjectionMetadataKeys.SchemaVersion] = synapse.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            [VectorProjectionMetadataKeys.NeuronContractId] = neuron.ContractId,
        };

        var text = new StringBuilder()
            .Append(synapse.ContractId)
            .Append(" v")
            .Append(synapse.SchemaVersion.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(synapse.Description);
        foreach (var example in synapse.Examples)
        {
            if (string.IsNullOrWhiteSpace(example))
            {
                continue;
            }

            text.Append(' ').Append(example);
        }

        return new VectorProjectionEntry(key, text.ToString(), metadata);
    }

    public static string SynapseKey(string contractId, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{contractId}@v{schemaVersion}");
    }
}
