namespace DigitalBrain.Runtime.Runtime;

// Payload is string-keyed/string-valued for v1 to match the in-tree
// DigitalBrain.InoLang Interpreter + INeuronHost. Contract-shaped marshalling
// is deferred — see docs/v3/VISION.md §6 (neuron boundary is not perf-sensitive).
[GenerateSerializer]
public sealed record SynapseEnvelope(
    [property: Id(0)] string TypeFqn,
    [property: Id(1)] IReadOnlyDictionary<string, string> Payload,
    [property: Id(2)] DateTimeOffset At);
