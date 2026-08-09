using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.Charting.Contracts;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Charting;

public sealed class ChartNeuron
{
    private readonly DurableTurn _turns;
    private readonly string _stateKey;

    public ChartNeuron(DurableTurn turns, string ownerId, string chartId)
    {
        _turns = turns ?? throw new ArgumentNullException(nameof(turns));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chartId);
        OwnerId = ownerId;
        ChartId = chartId;
        _stateKey = $"trusted-chart|{ownerId}|{chartId}";
    }

    public string OwnerId { get; }

    public string ChartId { get; }

    public Task HandleAsync(
        SynapseEnvelope envelope,
        CancellationToken cancellationToken = default)
        => HandleWithCommitAsync(envelope, cancellationToken);

    internal Task<bool> HandleWithCommitAsync(
        SynapseEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Synapse is not AddChartPoint command)
        {
            throw new ArgumentException("The trusted chart handler accepts only AddChartPoint.", nameof(envelope));
        }

        if (!string.Equals(envelope.OwnerId, OwnerId, StringComparison.Ordinal) ||
            !string.Equals(envelope.TargetScope, ChartId, StringComparison.Ordinal) ||
            !string.Equals(command.ChartId, ChartId, StringComparison.Ordinal))
        {
            throw new CapabilityDeniedException(typeof(AddChartPoint), command.ChartId);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(command.Draft.SourcePostId);
        return _turns.ExecuteTerminalFactWithCommitAsync<ChartState, ChartPointAdded>(
            envelope.DeliveryId,
            nameof(AddChartPoint),
            _stateKey,
            ChartState.Empty,
            state =>
            {
                var point = new ChartPoint(
                    command.Draft.SourcePostId,
                    command.Draft.OccurredAt,
                    state.Value.NextOrdinal);
                state.Replace(new ChartState(
                    checked(state.Value.NextOrdinal + 1),
                    [.. state.Value.Points, point]));
                return new ChartPointAdded(ChartId, point, envelope.DeliveryId);
            },
            cancellationToken);
    }

    public async Task<Snapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var state = await _turns.ReadStateAsync(_stateKey, ChartState.Empty, cancellationToken);
        return new Snapshot(ChartId, state.Points.ToArray());
    }

    public sealed record Snapshot(string ChartId, IReadOnlyList<ChartPoint> Points);

    private sealed record ChartState(int NextOrdinal, IReadOnlyList<ChartPoint> Points)
    {
        public static ChartState Empty { get; } = new(1, []);
    }
}
