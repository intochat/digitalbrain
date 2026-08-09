using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("surface")]
internal sealed class SurfaceNeuron :
    Neuron,
    ISurface,
    IHandle<OpenSurface>,
    IHandle<ControlActivated>,
    IEmit<SurfaceOpened>
{
    public Task HandleAsync(OpenSurface synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.SurfaceKey)
            || string.IsNullOrWhiteSpace(synapse.Title))
        {
            return Task.CompletedTask;
        }

        return EmitAsync(new SurfaceOpened(synapse.CommandId, Id, synapse.SurfaceKey, synapse.Title));
    }

    public Task HandleAsync(ControlActivated synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
