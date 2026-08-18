using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("chart")]
internal sealed class ChartNeuron : Neuron, IChart
{
    private const int RetainedPoints = 256;

    public async Task HandleAsync(ChartPoint synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        await GrantsNeuron.RequireReadAccessAsync(GrainFactory, Id, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await GrainFactory.GetGrain<IChartEntity>(EntityId.For<IChartEntity>(Id.Owner, Id.Name).ToGrainId())
            .Append(new ChartStatePoint(synapse.Series, synapse.Label, synapse.Value), RetainedPoints)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task<IReadOnlyList<ChartPoint>> Read()
    {
        await GrantsNeuron.RequireReadAccessAsync(GrainFactory, Id, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var state = await GrainFactory.GetGrain<IChartEntity>(EntityId.For<IChartEntity>(Id.Owner, Id.Name).ToGrainId())
            .Read()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return state is null
            ? []
            : [.. state.Points.Select(static point => new ChartPoint(point.Series, point.Label, point.Value))];
    }
}
