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

    public Task HandleAsync(ChartPoint synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        _points.Add(_serializer.SerializeToArray(synapse));
        while (_points.Count > RetainedPoints)
        {
            _points.RemoveAt(0);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChartPoint>> Read()
        => Task.FromResult<IReadOnlyList<ChartPoint>>([.. _points.Select(_serializer.Deserialize)]);
}
