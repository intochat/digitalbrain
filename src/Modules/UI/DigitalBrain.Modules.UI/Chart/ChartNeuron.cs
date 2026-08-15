using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.UI;

[GrainType("chart")]
internal sealed class ChartNeuron : Neuron, IChart
{
    private const string PointLogName = "chart.points";
    private const int RetainedPoints = 256;

    private readonly IDurableList<byte[]> _points;
    private readonly Serializer<ChartPoint> _serializer;

    public ChartNeuron()
    {
        _points = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(PointLogName);
        _serializer = ServiceProvider.GetRequiredService<Serializer<ChartPoint>>();
    }

    public async Task HandleAsync(ChartPoint synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        await GrantsNeuron.RequireReadAccessAsync(GrainFactory, Id, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _points.Add(_serializer.SerializeToArray(synapse));
        while (_points.Count > RetainedPoints)
        {
            _points.RemoveAt(0);
        }
    }

    public async Task<IReadOnlyList<ChartPoint>> Read()
    {
        await GrantsNeuron.RequireReadAccessAsync(GrainFactory, Id, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return [.. _points.Select(_serializer.Deserialize)];
    }
}
