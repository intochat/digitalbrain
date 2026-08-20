namespace DigitalBrain.UI;

// Append is silo-only; external callers may read through IEntity<TState> but cannot write here.
[Alias("ui.chart")]
public interface IChart : IEntity<ChartState>
{
    [Alias(nameof(Append))]
    Task Append(ChartStatePoint point, int cap);
}
