using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

// E-RUN #36 boundary helper. The gateway speaks strongly-typed Synapse records
// (one [GenerateSerializer] record per contract); the interpreted runtime
// speaks the generic SynapseEnvelope (TypeFqn + string-keyed Payload). When
// the Navigator routes a synapse to an interpreted neuron, this helper flips
// the representation at the boundary.
//
// DeclaredOnly is the same filter AssemblyScanningContractCatalog uses to
// drop the inherited Synapse envelope fields (SynapseId, CorrelationId, …):
// only the domain-shaped fields the contract declares cross to the
// interpreter. v1 payload is string-keyed/string-valued per
// SynapseEnvelope.cs — full marshalling lands with E-RUN #37 (Cortex
// broadcast + signal-log fan-out) when the boundary becomes load-bearing.
public static class SynapseEnvelopeConverter
{
    public static SynapseEnvelope From(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var type = synapse.GetType();
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (property.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) continue;
            var value = property.GetValue(synapse);
            if (value is null) continue;
            payload[property.Name] = value.ToString() ?? "";
        }

        return new SynapseEnvelope(type.FullName!, payload, synapse.Timestamp);
    }
}
