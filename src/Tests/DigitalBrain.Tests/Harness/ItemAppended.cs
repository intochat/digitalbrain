using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Tests.Harness;

[GenerateSerializer]
[Alias("ui.item-appended")]
[Description("Generic UI vocabulary: append an item to whatever control receives it")]
public sealed record ItemAppended([property: Id(0)] string Title) : Synapse;

internal sealed class ProbeFactToItemAppended : ISynapseTransform
{
    internal const string TransformName = "probe.fact->ui.item-appended";

    public string Name => TransformName;

    public Synapse Apply(Synapse synapse)
        => synapse is ProbeFact fact
            ? new ItemAppended(fact.Text)
            : throw new InvalidOperationException(
                $"Transform '{Name}' cannot adapt a '{synapse.GetType().Name}'.");
}

internal sealed class ProbeFactToChartPoint : ISynapseTransform
{
    internal const string TransformName = "probe.fact->ui.chart-point";

    public string Name => TransformName;

    public Synapse Apply(Synapse synapse)
        => synapse is ProbeFact fact
            ? new DigitalBrain.UI.ChartPoint("posts", fact.Text, 1)
            : throw new InvalidOperationException(
                $"Transform '{Name}' cannot adapt a '{synapse.GetType().Name}'.");
}

internal sealed class PoisonTransform : ISynapseTransform
{
    internal const string TransformName = "probe.poison";

    public string Name => TransformName;

    public Synapse Apply(Synapse synapse)
        => throw new InvalidOperationException($"Transform '{Name}' always fails.");
}
