namespace DigitalBrain.Kernel;

internal sealed class DispatchManifest(IReadOnlyList<SynapseWiringEntry> handlers)
{
    internal IReadOnlyList<SynapseWiringEntry> Handlers { get; } = handlers;
}
