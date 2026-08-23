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
}
