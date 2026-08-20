using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("uirenderer")]
internal sealed class UIRenderer : Neuron, IUIRenderer
{
    private const int RetainedScenes = 64;

    public async Task HandleAsync(OpenSurface synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.SurfaceKey)
            || string.IsNullOrWhiteSpace(synapse.Title))
        {
            return;
        }

        var surface = EntityId.For<ISurface>(Id.Owner, Id.Name);
        await GrainFactory
            .GetGrain<ISurface>(surface.ToGrainId())
            .Open(new SurfaceScene(synapse.SurfaceKey, synapse.Title), RetainedScenes)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await EmitAsync(new SurfaceOpened(synapse.CommandId, Id, synapse.SurfaceKey, synapse.Title))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(ControlActivated synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
