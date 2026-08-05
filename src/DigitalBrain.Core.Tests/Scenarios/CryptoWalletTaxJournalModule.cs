using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record OnChainTransferObserved(
    string TxHash,
    string Asset,
    decimal Amount,
    string Direction,
    string Counterparty,
    DateTimeOffset BlockTime) : Synapse;

public sealed record HistoricalPriceAsked(
    string Asset,
    DateTimeOffset At,
    string TxHash) : Synapse;

public sealed record HistoricalPriceAnswered(
    string Asset,
    DateTimeOffset At,
    string TxHash,
    decimal PriceUsd) : Synapse;

public sealed record TaxLotOpened(
    string LotId,
    string TxHash,
    string Asset,
    decimal Qty,
    decimal BasisUsd) : Synapse;

public sealed record TaxLotDisposed(
    string DisposalId,
    string TxHash,
    string Asset,
    decimal Qty,
    decimal ProceedsUsd,
    decimal GainUsd,
    string Method,
    ImmutableArray<string> ConsumedLots) : Synapse;

public sealed record TaxLotDeskNote(string TxHash, string Kind) : Synapse;

public sealed class TaxLotsState
{
#pragma warning disable CA1002, CA2227
    public List<OpenTaxLot> OpenLots { get; set; } = [];
#pragma warning restore CA1002, CA2227
    public string? PendingTxHash { get; set; }
    public string? PendingAsset { get; set; }
    public decimal PendingAmount { get; set; }
    public string? PendingDirection { get; set; }
}

public sealed class OpenTaxLot
{
    public string LotId { get; set; } = "";
    public string TxHash { get; set; } = "";
    public string Asset { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal BasisPerUnit { get; set; }
}

// Ordered tax books: inbound opens a lot after price ask; outbound FIFO-disposes open lots.
public sealed class TaxLots : Neuron<TaxLotsState>,
    INeuron<OnChainTransferObserved>,
    INeuron<HistoricalPriceAnswered>
{
    public Task HandleAsync(OnChainTransferObserved fact, CancellationToken cancellationToken)
    {
        if (State.PendingTxHash is not null)
        {
            return Task.CompletedTask;
        }

        State.PendingTxHash = fact.TxHash;
        State.PendingAsset = fact.Asset;
        State.PendingAmount = fact.Amount;
        State.PendingDirection = fact.Direction;
        Ask<HistoricalPriceAnswered>(new HistoricalPriceAsked(fact.Asset, fact.BlockTime, fact.TxHash));
        return Task.CompletedTask;
    }

    public Task HandleAsync(HistoricalPriceAnswered fact, CancellationToken cancellationToken)
    {
        if (State.PendingTxHash is null
            || !string.Equals(State.PendingTxHash, fact.TxHash, StringComparison.Ordinal)
            || State.PendingAsset is null
            || State.PendingDirection is null)
        {
            return Task.CompletedTask;
        }

        var amount = State.PendingAmount;
        var asset = State.PendingAsset;
        var txHash = State.PendingTxHash;
        var direction = State.PendingDirection;
        State.PendingTxHash = null;
        State.PendingAsset = null;
        State.PendingDirection = null;

        if (string.Equals(direction, "in", StringComparison.OrdinalIgnoreCase))
        {
            var lotId = $"lot-{txHash}";
            State.OpenLots.Add(new OpenTaxLot
            {
                LotId = lotId,
                TxHash = txHash,
                Asset = asset,
                Qty = amount,
                BasisPerUnit = fact.PriceUsd,
            });
            Emit(new TaxLotOpened(lotId, txHash, asset, amount, BasisUsd: amount * fact.PriceUsd));
            Emit(new TaxLotDeskNote(txHash, "opened"));
            return Task.CompletedTask;
        }

        // FIFO dispose against open lots for the asset.
        var remaining = amount;
        var consumed = new List<string>();
        decimal totalBasis = 0;
        while (remaining > 0 && State.OpenLots.Count > 0)
        {
            var lot = State.OpenLots[0];
            if (!string.Equals(lot.Asset, asset, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var take = Math.Min(remaining, lot.Qty);
            totalBasis += take * lot.BasisPerUnit;
            consumed.Add(lot.LotId);
            lot.Qty -= take;
            remaining -= take;
            if (lot.Qty <= 0)
            {
                State.OpenLots.RemoveAt(0);
            }
        }

        var proceeds = amount * fact.PriceUsd;
        Emit(new TaxLotDisposed(
            DisposalId: $"disp-{txHash}",
            TxHash: txHash,
            Asset: asset,
            Qty: amount,
            ProceedsUsd: proceeds,
            GainUsd: proceeds - totalBasis,
            Method: "FIFO",
            ConsumedLots: [.. consumed]));
        Emit(new TaxLotDeskNote(txHash, "disposed"));
        return Task.CompletedTask;
    }
}

// Deterministic historical price answerer — no network.
public sealed class TaxPriceOracle : Neuron, IAnswers<HistoricalPriceAsked, HistoricalPriceAnswered>
{
    public Task<HistoricalPriceAnswered?> HandleAsync(
        HistoricalPriceAsked question, CancellationToken cancellationToken)
    {
        var price = string.Equals(question.Asset, "BTC", StringComparison.OrdinalIgnoreCase)
            ? 100_000m
            : 3_000m;
        return Task.FromResult<HistoricalPriceAnswered?>(
            new HistoricalPriceAnswered(question.Asset, question.At, question.TxHash, price));
    }
}

// Catalog sinks for ambient lot facts.
public sealed class TaxLotLedger : Neuron,
    INeuron<TaxLotOpened>,
    INeuron<TaxLotDisposed>,
    INeuron<TaxLotDeskNote>
{
    public Task HandleAsync(TaxLotOpened fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(TaxLotDisposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(TaxLotDeskNote fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
