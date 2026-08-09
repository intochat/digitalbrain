using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias(AliasName)]
[Description("Generic UI vocabulary: append one point to whatever chart receives it")]
public sealed record ChartPoint(
    [property: Id(0)] string Series,
    [property: Id(1)] string Label,
    [property: Id(2)] double Value) : Synapse
{
    public const string AliasName = "ui.chart-point";
}
