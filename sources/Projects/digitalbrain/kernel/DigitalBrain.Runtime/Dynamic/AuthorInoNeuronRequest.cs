using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

// E-SDK #57 close-out. The synapse entry into the InoLang-retargeted
// Creator. A caller (gateway / intent dispatcher / a future MCP bridge)
// sends this to the silo's InoCreatorNeuron; the grain delegates the
// authoring loop to InoAuthoringLoop and broadcasts a
// `DigitalBrain.Creator.InoNeuronAuthored` signal carrying the outcome
// (FQN + persisted relative path + attempt count + status) so observers
// — BrainWatch, the home feed, a Marketplace-publish gate — can react
// without coupling to the kernel's InoAuthoring internals.
//
// Lives in DigitalBrain.Domains.Dynamic.Contracts (not the kernel) because
// the existing CreateNeuronRequest / NeuronCreated synapses for the
// parallel C#-triplet authoring path live here too — keeping the
// Creator's public contract surface in one place. The two surfaces
// coexist permanently per CLAUDE.md's locked decision D-A: DigitalBrain
// stays the platform substrate and the C# triplet remains the
// DigitalBrain Engineering authoring surface alongside InoLang. The
// handler grain lives in DigitalBrain.Kernel.Creator.InoAuthoring per the
// "Creator is kernel infrastructure" rule (it's not a domain neuron).
[GenerateSerializer]
public sealed record AuthorInoNeuronRequest([property: Id(1)] string Intent,
    [property: Id(2)] string SuggestedFqn,
    [property: Id(3)] string LlmModelKey,
    [property: Id(4)] int MaxAttempts
) : Synapse;
