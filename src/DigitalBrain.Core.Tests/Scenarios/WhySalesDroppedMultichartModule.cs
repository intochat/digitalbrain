using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

// Seeded metric fact — WhySalesAnswer may cite only these journaled series, never free invention.
public sealed record MetricObserved(
    string MetricId,
    string Series,
    double Value,
    string Unit) : Synapse;

public sealed record SalesDropAsked(string Period) : Synapse;

public sealed record WhySalesAnswer(
    string Period,
    string Narrative,
    ImmutableArray<string> CitedMetricIds,
    ImmutableArray<string> CitedSeries) : Synapse;

public sealed class SalesDiagnosticState
{
#pragma warning disable CA1002, CA2227, CA1819
    public List<MetricObserved> Metrics { get; set; } = [];
#pragma warning restore CA1002, CA2227, CA1819
}

// Diagnostic: metrics must be journaled first; SalesDropAsked fans ≥2 ChartSpec + WhySalesAnswer from those facts.
public sealed class SalesDiagnostic : Neuron<SalesDiagnosticState>,
    INeuron<MetricObserved>,
    INeuron<SalesDropAsked>
{
    public Task HandleAsync(MetricObserved fact, CancellationToken cancellationToken)
    {
        State.Metrics.Add(fact);
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesDropAsked fact, CancellationToken cancellationToken)
    {
        if (State.Metrics.Count < 2)
        {
            throw new InvalidOperationException(
                "SalesDropAsked requires at least two prior journaled MetricObserved facts.");
        }

        var ordered = State.Metrics
            .OrderBy(metric => metric.MetricId, StringComparer.Ordinal)
            .ToArray();

        // Progressive multi-chart: one ChartSpec per seeded series (acceptance: ≥2).
        foreach (var metric in ordered)
        {
            Emit(new ChartSpec(
                ChartId: $"{fact.Period}-{metric.Series}",
                Title: $"{metric.Series} ({fact.Period})",
                Series: [metric.Series, metric.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
        }

        var citedIds = ordered.Select(metric => metric.MetricId).ToImmutableArray();
        var citedSeries = ordered.Select(metric => metric.Series).ToImmutableArray();
        var narrative =
            $"Sales drop in {fact.Period}: "
            + string.Join(
                "; ",
                ordered.Select(metric =>
                    $"{metric.Series}={metric.Value}{metric.Unit} (id {metric.MetricId})"));

        Emit(new WhySalesAnswer(fact.Period, narrative, citedIds, citedSeries));
        return Task.CompletedTask;
    }
}

// Shell canvas hears charts + narrative as separate ambient facts.
public sealed class SalesDiagnosticUi : Neuron, INeuron<ChartSpec>, INeuron<WhySalesAnswer>
{
    public Task HandleAsync(ChartSpec fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WhySalesAnswer fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
