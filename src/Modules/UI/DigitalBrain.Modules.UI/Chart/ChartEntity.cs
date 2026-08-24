using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType("chart")]
internal sealed class ChartEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChartState> state)
    : Entity<ChartState>(state), IChart
{
    public async Task Render(ChartState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        await SaveAsync(state);
    }

    public async Task Append(ChartPoint point, string title)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var current = State ?? new ChartState(title.Trim(), "line", []);
        if (!string.IsNullOrWhiteSpace(point.EventId)
            && current.Points.Any(existing => string.Equals(existing.EventId, point.EventId, StringComparison.Ordinal)))
        {
            return;
        }

        await SaveAsync(current with { Points = [.. current.Points, point] });
    }
}
