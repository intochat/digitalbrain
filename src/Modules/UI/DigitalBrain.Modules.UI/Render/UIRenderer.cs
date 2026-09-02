using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("uirenderer")]
internal sealed class UIRenderer(NeuronRuntime runtime) : Neuron(runtime), IUIRenderer
{
    private const int RetainedScenes = 64;

    public async Task HandleAsync(OpenSurface signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(signal.SurfaceKey)
            || string.IsNullOrWhiteSpace(signal.Title))
        {
            return;
        }

        var surface = EntityId.For<ISurface>(Id.Owner, Id.Name);
        await GrainFactory
            .GetGrain<ISurface>(surface.ToGrainId())
            .Open(new SurfaceScene(signal.SurfaceKey, signal.Title), RetainedScenes)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await RecordOutgoingAsync(new SurfaceOpened(signal.CommandId, Id, signal.SurfaceKey, signal.Title))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(ControlActivated signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
