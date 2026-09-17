using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter;

[Alias("ui.surface")]
public interface ISurface : INeuron
{
    /// <summary>Opens a scene. The receipt is advisory - what the caller can show at once; the reaction recomputes the authoritative one and publishes it on SurfaceOpened.</summary>
    [Alias("open")]
    [NeuronTool]
    Task<Accepted<SurfaceOpenReceipt>> Open(OpenSurface command, CancellationToken cancellationToken = default);

    /// <summary>Activates a button in the current scene.</summary>
    [Alias("activate")]
    [NeuronTool]
    Task<Accepted<ControlActivation>> Activate(ActivateControl command, CancellationToken cancellationToken = default);

    /// <summary>Reads the surface scenes, activities, and receipts.</summary>
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<SurfaceState> Read();
}
