using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. The on-disk manifest that sits alongside a
// Creator-authored `.ino` under
// `src/domains/dynamic/.../Generated/<neuron-id>/`. Carries the metadata
// `DynamicGeneratedInoSource` consumes routing fields at silo start without
// loading the `.ino` body. FQN is redundant with the `neuron <FQN>` line but
// lets discovery publish routing metadata while the body remains lazy.
//
// `CreatedAtUtc` is the authoring timestamp (round-trip stable via
// `"O"` format); `CreatorLlmModel` is what the Creator's own loop ran
// against, NOT what the authored neuron uses as a neuron. The
// authored-neuron neuron key lives inside the `.ino`'s
// `using $... = neuron(... [\"<key>\"])` line.
public sealed record InoNeuronManifest(
    string Fqn,
    string NeuronId,
    string SourceFileName,
    string Intent,
    string CreatorLlmModel,
    string CreatedAtUtc,
    IReadOnlyList<IncomingPort>? Incoming = null,
    IReadOnlyList<string>? Outgoing = null,
    IReadOnlyList<string>? HandledSignalSubscriptions = null,
    string? SourceSha256 = null);
