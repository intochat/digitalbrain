using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias(AliasName)]
public sealed record ChartPoint(
    [property: Id(0)] string Series,
    [property: Id(1)] string Label,
    [property: Id(2)] double Value) : Synapse
{
    public const string AliasName = "ui.chart-point";
}
