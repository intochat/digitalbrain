using DigitalBrain.Core;

namespace DigitalBrain.UI;

[GrainType("chartentity")]
internal sealed class ChartEntity : Entity<ChartState>, IChartEntity
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
