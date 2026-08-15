using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[ClientEntryPoint]
[Alias("ui.chart")]
public partial interface IChart : INeuron, IHandle<ChartPoint>
{
    [Alias(nameof(Read))]
    Task<IReadOnlyList<ChartPoint>> Read();
}
