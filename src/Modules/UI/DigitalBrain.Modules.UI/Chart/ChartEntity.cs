using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType("chart")]
internal sealed class ChartEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChartState> state)
    : Entity<ChartState>(state), IChart
{
    public async Task Append(ChartStatePoint point, int cap)
    {
        ArgumentNullException.ThrowIfNull(point);

        var points = (State?.Points ?? []).ToList();
        points.Add(point);
        while (points.Count > cap)
        {
            points.RemoveAt(0);
        }

        await SaveAsync(new ChartState(points));
    }
}
