namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// Freezes one query identity, validates returned provider data, and publishes
/// exactly one immutable domain result.
/// </summary>
public sealed class SalesInsightNeuron : Neuron<SalesInsightState>,
    INeuron<SalesInsightRequested>,
    INeuron<SalesRevenueReadCompleted>,
    INeuron<SalesRevenueReadUnavailable>
{
    public const string Kind = "sales-insight";
    private const string ConversationIngressKind = "conversation-ingress";

    public Task HandleAsync(SalesInsightRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesQuery(synapse.Query)
            || !IsTrustedConversationStart(synapse.Context)
            || State.Query is not null)
        {
            return Task.CompletedTask;
        }

        var state = State;
        state.Query = synapse.Query;
        state.Context = synapse.Context;
        State = state;
        Emit(
            new SalesRevenueReadRequested(synapse.Query),
            Dispatch.Direct(new NeuronId(SalesInsightEffectNeuron.Kind, synapse.Query.QueryId)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesRevenueReadCompleted synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var state = State;
        if (!MatchesReaderResult(state, synapse.Query))
        {
            return Task.CompletedTask;
        }

        if (!TryCreateResult(state.Query!, state.Context!, synapse.Records, out var result))
        {
            FinalizeUnavailable(state, SalesInsightUnavailableReason.InvalidReaderData);
            return Task.CompletedTask;
        }

        state.Result = result;
        state.Finalized = true;
        State = state;
        Emit(new SalesInsightReady(result));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesRevenueReadUnavailable synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var state = State;
        if (state.Query is null
            || state.Context is null
            || state.Finalized
            || !string.Equals(Id.Name, synapse.QueryId, StringComparison.Ordinal)
            || Origin.Source != new NeuronId(SalesInsightEffectNeuron.Kind, synapse.QueryId))
        {
            return Task.CompletedTask;
        }

        FinalizeUnavailable(state, synapse.Reason);
        return Task.CompletedTask;
    }

    private bool MatchesQuery(SalesQuery query)
        => string.Equals(Id.Name, query.QueryId, StringComparison.Ordinal);

    private bool IsTrustedConversationStart(SalesInsightContext context)
        => Origin.Source.Kind == ConversationIngressKind
            && context.Kind == SalesInsightContextKind.ChatConversation
            && string.Equals(context.Reference, Origin.Source.Name, StringComparison.Ordinal);

    private bool MatchesReaderResult(SalesInsightState state, SalesQuery query)
        => state.Query is not null
            && state.Context is not null
            && !state.Finalized
            && Equals(state.Query, query)
            && Origin.Source == new NeuronId(SalesInsightEffectNeuron.Kind, query.QueryId);

    private void FinalizeUnavailable(SalesInsightState state, SalesInsightUnavailableReason reason)
    {
        var query = state.Query ?? throw new InvalidOperationException("A sales insight unavailable outcome needs a query.");
        var context = state.Context ?? throw new InvalidOperationException("A sales insight unavailable outcome needs a context.");
        state.Finalized = true;
        state.UnavailableReason = reason;
        State = state;
        Emit(new SalesInsightUnavailable(query.QueryId, context, reason));
    }

    private static bool TryCreateResult(
        SalesQuery query,
        SalesInsightContext context,
        IReadOnlyList<SalesRevenueRecord> records,
        out SalesInsightResult result)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count > SalesInsightLimits.MaximumReaderRecords
            || records.Any(record => record is null
            || !query.Range.Contains(record.ClosedOn)
            || !string.Equals(record.CurrencyCode, query.CurrencyCode, StringComparison.Ordinal)
            || record.Amount < 0))
        {
            result = null!;
            return false;
        }

        try
        {
            var totals = records
                .GroupBy(static record => record.ClosedOn)
                .ToDictionary(
                    static group => group.Key,
                    static group => (Amount: group.Sum(static record => record.Amount), Count: group.Count()));
            var buckets = new List<SalesRevenueBucket>();
            for (var date = query.Range.FromInclusive; date < query.Range.ToExclusive; date = date.AddDays(1))
            {
                var (amount, count) = totals.GetValueOrDefault(date);
                buckets.Add(new SalesRevenueBucket(date, amount, count));
            }

            result = new SalesInsightResult(
                query,
                context,
                buckets,
                records.Sum(static record => record.Amount),
                records.Count);
            return true;
        }
        catch (OverflowException)
        {
            result = null!;
            return false;
        }
    }
}
