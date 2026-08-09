using System.Text.Json;
using DigitalBrain.Poc.Charting;
using DigitalBrain.Poc.Charting.Contracts;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Host;

public sealed class TrustedChartOutboxDrain
{
    private readonly DurableTurn _turns;
    private readonly ChartProjectionEndpoint _charts;
    private readonly Func<CancellationToken, Task>? _afterChartCommit;

    public TrustedChartOutboxDrain(
        DurableTurn turns,
        ChartProjectionEndpoint charts,
        Func<CancellationToken, Task>? afterChartCommit = null)
    {
        _turns = turns ?? throw new ArgumentNullException(nameof(turns));
        _charts = charts ?? throw new ArgumentNullException(nameof(charts));
        _afterChartCommit = afterChartCommit;
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var pending = await _turns.ReadPendingTrustedTargetOutboxAsync(cancellationToken);
            if (pending.Count == 0)
            {
                return;
            }

            foreach (var item in pending)
            {
                var command = DeserializeAddChartPoint(item);
                var envelope = SynapseEnvelope.RestoreTrustedTarget(
                    item.DeliveryId,
                    item.OwnerId,
                    item.ContractAlias,
                    command,
                    item.Family,
                    item.ProducingRevision,
                    item.ProducingModuleIdentity,
                    item.TargetScope);

                var committed = await _charts.DeliverTrustedTargetWithCommitAsync(
                    envelope,
                    cancellationToken);
                if (committed && _afterChartCommit is not null)
                {
                    await _afterChartCommit(cancellationToken);
                }

                await _turns.MarkOutboxDeliveredAsync(item.DeliveryId, cancellationToken);
            }
        }
    }

    public async Task ReplayLastCommittedAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        var item = await new Outbox(_turns.Root).ReadLastTrustedTargetAsync(
            ownerId,
            cancellationToken) ??
            throw new InvalidOperationException("No trusted chart delivery has been committed.");
        var command = DeserializeAddChartPoint(item);
        var envelope = SynapseEnvelope.RestoreTrustedTarget(
            item.DeliveryId,
            item.OwnerId,
            item.ContractAlias,
            command,
            item.Family,
            item.ProducingRevision,
            item.ProducingModuleIdentity,
            item.TargetScope);
        var committed = await _charts.DeliverTrustedTargetWithCommitAsync(
            envelope,
            cancellationToken);
        if (committed && _afterChartCommit is not null)
        {
            await _afterChartCommit(cancellationToken);
        }
    }

    private static AddChartPoint DeserializeAddChartPoint(PendingTrustedTargetOutboxEnvelope item)
    {
        if (!string.Equals(item.Kind, nameof(AddChartPoint), StringComparison.Ordinal) ||
            !string.Equals(item.ContractAlias, ContractAlias.For(typeof(AddChartPoint)), StringComparison.Ordinal) ||
            !string.Equals(item.PayloadFormat, "json", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Committed trusted target payload '{item.DeliveryId}' is not an AddChartPoint JSON payload.");
        }

        var command = JsonSerializer.Deserialize<AddChartPoint>(Convert.FromBase64String(item.PayloadBase64)) ??
            throw new InvalidDataException(
                $"Committed trusted target payload '{item.DeliveryId}' deserialized to null.");
        if (!string.Equals(command.ChartId, item.TargetScope, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Committed trusted target payload '{item.DeliveryId}' does not match its persisted target scope.");
        }

        return command;
    }
}
