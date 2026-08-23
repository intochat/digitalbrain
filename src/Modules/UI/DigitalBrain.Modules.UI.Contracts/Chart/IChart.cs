using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.UI;

// Same wall as ISurface: Read() is the client-facing query via IEntity<TState>;
// Render stays a same-silo grain call (kit tools drive it).
[Alias("ui.chart")]
public interface IChart : IEntity<ChartState>
{
    [Alias(nameof(Render))]
    Task Render(ChartState state);
}
