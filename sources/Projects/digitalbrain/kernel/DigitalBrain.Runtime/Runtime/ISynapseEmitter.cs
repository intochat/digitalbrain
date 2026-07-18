namespace DigitalBrain.Runtime.Runtime;

// E-SDK #60. The kernel-provided emit facade for L3 SDK connector neurons
// (v3 §L7: "ingress signals are produced by SDK connector neurons"). A
// connector receives this via DI and fires a system/ingress signal without
// reaching into SynapseBroadcaster or the gateway directly — that decoupling
// is the whole point of the neuron, since the connector lives in
// DigitalBrain.SDK.* and must not depend on the kernel's Cortex internals.
//
// Mirrors SynapseBroadcaster.BroadcastSystemSignalAsync (no authoring plan —
// the connector has no .ino with declared signal ports). The implementation
// in DigitalBrain.Kernel adapts to the broadcaster; tests can substitute a
// recording fake.
public interface ISynapseEmitter
{
    Task EmitAsync(
        string fqn,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken);
}
