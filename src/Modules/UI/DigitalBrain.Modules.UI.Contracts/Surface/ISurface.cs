using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.UI;

[Alias("ui.surface")]
public interface ISurface : INeuron
{
    /// <summary>Opens a scene and returns its receipt.</summary>
    [Alias("open")]
    Task<Accepted<SurfaceOpenReceipt>> Open(OpenSurface command, CancellationToken cancellationToken = default);

    /// <summary>Activates a button in the current scene.</summary>
    [Alias("activate")]
    Task<Accepted<ControlActivation>> Activate(ActivateControl command, CancellationToken cancellationToken = default);

    /// <summary>Reads the surface scenes, activities, and receipts.</summary>
    [ReadOnly, Alias("read")]
    Task<SurfaceState> Read();
}
