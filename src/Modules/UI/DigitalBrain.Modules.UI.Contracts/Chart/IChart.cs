using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.UI;

[Alias("ui.chart")]
public interface IChart : IEntity<ChartState>
{
    [Alias(nameof(Render))]
    Task Render(ChartState state);
}
