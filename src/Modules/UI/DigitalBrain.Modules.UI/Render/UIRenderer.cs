using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("uirenderer")]
internal sealed class UIRenderer : Neuron, IUIRenderer
{
    private const int RetainedPoints = 256;
    private const int RetainedScenes = 64;

    public async Task HandleAsync(ChartPoint synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        // The grant subject is the chart being written, not this renderer: grants are issued
        // against "chart:{owner}/{name}", and the chart shares this instance's name.
        var chart = EntityId.For<IChart>(Id.Owner, Id.Name);
        await GrantsNeuron.RequireReadAccessAsync(
                GrainFactory,
                new NeuronId(chart.Type, chart.Owner, chart.Name),
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await GrainFactory.GetGrain<IChart>(chart.ToGrainId())
            .Append(new ChartStatePoint(synapse.Series, synapse.Label, synapse.Value), RetainedPoints)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        // This write is silo-side (GrainFactory, not the client facade's GetEntity), so the
        // brain never learns about it unless the renderer tells it here.
        _ = RegisterInOwnersBrainAsync(
            new BrainReference(BrainReferenceKind.Entity, chart.Type, chart.Name, default));
    }

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

        // This write is silo-side (GrainFactory, not the client facade's GetEntity), so the
        // brain never learns about it unless the renderer tells it here.
        _ = RegisterInOwnersBrainAsync(
            new BrainReference(BrainReferenceKind.Entity, surface.Type, surface.Name, default));

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
