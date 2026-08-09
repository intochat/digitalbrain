using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.Charting.Contracts;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Charting;

public sealed class ChartProjectionEndpoint
{
    private readonly IReadOnlyDictionary<string, ChartNeuron> _charts;

    public ChartProjectionEndpoint(IEnumerable<ChartNeuron> charts)
    {
        ArgumentNullException.ThrowIfNull(charts);
        var registered = charts.ToArray();
        _charts = registered.ToDictionary(
            chart => Key(chart.OwnerId, chart.ChartId),
            StringComparer.Ordinal);
    }

    public Task<ChartNeuron.Snapshot?> ReadAsync(
        string ownerId,
        string chartId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chartId);
        return _charts.TryGetValue(Key(ownerId, chartId), out var chart)
            ? ReadRegisteredAsync(chart, cancellationToken)
            : Task.FromResult<ChartNeuron.Snapshot?>(null);
    }

    public Task DeliverTrustedTargetAsync(
        SynapseEnvelope envelope,
        CancellationToken cancellationToken = default)
        => DeliverTrustedTargetWithCommitAsync(envelope, cancellationToken);

    public Task<bool> DeliverTrustedTargetWithCommitAsync(
        SynapseEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.TargetRevision is not null ||
            envelope.TargetModuleIdentity is not null ||
            string.IsNullOrWhiteSpace(envelope.TargetScope) ||
            !_charts.TryGetValue(Key(envelope.OwnerId, envelope.TargetScope), out var chart))
        {
            throw new CapabilityDeniedException(typeof(AddChartPoint), envelope.TargetScope ?? string.Empty);
        }

        return chart.HandleWithCommitAsync(envelope, cancellationToken);
    }

    private static async Task<ChartNeuron.Snapshot?> ReadRegisteredAsync(
        ChartNeuron chart,
        CancellationToken cancellationToken) =>
        await chart.ReadAsync(cancellationToken);

    private static string Key(string ownerId, string chartId) => $"{ownerId.Length}:{ownerId}{chartId}";
}
