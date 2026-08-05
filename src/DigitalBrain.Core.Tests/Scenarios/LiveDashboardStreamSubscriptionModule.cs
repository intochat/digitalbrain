namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record DashboardSubscriptionAttached(
    string SceneId,
    string MetricsNeuronKind) : Synapse;

public sealed record DashboardSnapshot(
    string SceneId,
    double ClosedWonToday,
    double CashIn,
    int OpenPos) : Synapse;

public sealed record OpportunityClosedWon(
    string OppId,
    string Account,
    double Amount) : Synapse;

public sealed record InvoicePaid(
    string InvoiceId,
    double Amount,
    string Currency) : Synapse;

public sealed record PurchaseOrderEmailDetected(
    string MessageId,
    string Vendor,
    double AmountHint) : Synapse;

public sealed record KpiTileUpdated(
    string Tile,
    double Value,
    int Revision) : Synapse;

public sealed record RevenueChartPointAppended(
    string Series,
    double Amount,
    string SourceFactKind) : Synapse;

public sealed class RevenueMetricsState
{
    public string? SceneId { get; set; }
    public double ClosedWonToday { get; set; }
    public double CashIn { get; set; }
    public int OpenPos { get; set; }
    public int Revision { get; set; }
    public bool Attached { get; set; }
}

// Live revenue pulse: subscription snapshot then ambient domain facts revise tiles + chart.
public sealed class RevenueMetricsProjector : Neuron<RevenueMetricsState>,
    INeuron<DashboardSubscriptionAttached>,
    INeuron<OpportunityClosedWon>,
    INeuron<InvoicePaid>,
    INeuron<PurchaseOrderEmailDetected>
{
    public Task HandleAsync(DashboardSubscriptionAttached fact, CancellationToken cancellationToken)
    {
        State.SceneId = fact.SceneId;
        State.Attached = true;
        State.Revision = 1;
        Emit(new DashboardSnapshot(
            fact.SceneId,
            State.ClosedWonToday,
            State.CashIn,
            State.OpenPos));
        return Task.CompletedTask;
    }

    public Task HandleAsync(OpportunityClosedWon fact, CancellationToken cancellationToken)
    {
        if (!State.Attached)
        {
            return Task.CompletedTask;
        }

        State.ClosedWonToday += fact.Amount;
        State.Revision++;
        Emit(new KpiTileUpdated("closedWonToday", State.ClosedWonToday, State.Revision));
        Emit(new RevenueChartPointAppended("revenue", fact.Amount, nameof(OpportunityClosedWon)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(InvoicePaid fact, CancellationToken cancellationToken)
    {
        if (!State.Attached)
        {
            return Task.CompletedTask;
        }

        State.CashIn += fact.Amount;
        State.Revision++;
        Emit(new KpiTileUpdated("cashIn", State.CashIn, State.Revision));
        Emit(new RevenueChartPointAppended("cash", fact.Amount, nameof(InvoicePaid)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderEmailDetected fact, CancellationToken cancellationToken)
    {
        if (!State.Attached)
        {
            return Task.CompletedTask;
        }

        State.OpenPos++;
        State.Revision++;
        Emit(new KpiTileUpdated("openPOs", State.OpenPos, State.Revision));
        return Task.CompletedTask;
    }
}

// UI edge hears snapshot, tiles, chart points (ambient catalog listeners).
public sealed class UiEdgeDashboard : Neuron,
    INeuron<DashboardSnapshot>,
    INeuron<KpiTileUpdated>,
    INeuron<RevenueChartPointAppended>
{
    public Task HandleAsync(DashboardSnapshot fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(KpiTileUpdated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(RevenueChartPointAppended fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
