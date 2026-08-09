using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[ClientEntryPoint]
[Alias("ui.chart")]
[Description("Chart control with identity; renders whatever points are routed at it")]
public partial interface IChart : INeuron, IHandle<ChartPoint>
{
    [Alias(nameof(Read))]
    Task<IReadOnlyList<ChartPoint>> Read();
}
