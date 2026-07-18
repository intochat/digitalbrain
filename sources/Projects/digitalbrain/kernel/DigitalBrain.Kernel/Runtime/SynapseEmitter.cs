using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

// E-SDK #60. The kernel-side adapter that fulfils the ISynapseEmitter
// contract by delegating to SynapseBroadcaster's port-less system-signal
// path. L3 SDK connector neurons (DigitalBrain.SDK.Aspire and the
// connector family beyond it) consume ISynapseEmitter via DI; the
// broadcaster, navigator and signal log stay private to the kernel.
public sealed class SynapseEmitter(SynapseBroadcaster broadcaster) : ISynapseEmitter
{
    public Task EmitAsync(
        string fqn,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken) =>
        broadcaster.BroadcastSystemSignalAsync(fqn, payload, cancellationToken);
}
