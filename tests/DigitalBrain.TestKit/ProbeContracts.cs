// Physically lives in DigitalBrain.TestKit (test-only infrastructure, never referenced from production
// code) but declares the DigitalBrain.Core namespace, matching every other neuron interface/synapse in
// this codebase (see src/DigitalBrain.Core/Synapse.cs) and satisfying CapabilityGate.AllowedNamespacePrefixes
// (only "System." and "DigitalBrain.Core." are allowed inside dynamically-compiled pack sources) so tests
// that fire this synapse from an embodied pack's typed dispatch checks (e.g. "synapse is ProbeMessageSynapse")
// don't get rejected by the same capability sandbox real typed bundles are compiled under.
namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Core.ProbeMessageSynapse")]
public record ProbeMessageSynapse(string Text) : Synapse(nameof(ProbeMessageSynapse), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.IProbeNeuron")]
public interface IProbeNeuron : INeuron
{
}
