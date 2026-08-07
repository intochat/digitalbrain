using DigitalBrain.Product.SalesInsights;

namespace DigitalBrain.Product.Presentation;

/// <summary>
/// Projects a trusted immutable sales result to Base UI Kit semantics without
/// acquiring a provider or workflow responsibility.
/// </summary>
public sealed class SalesInsightProjectionNeuron : Neuron<SalesInsightProjectionState>,
    INeuron<SalesInsightReady>,
    INeuron<SalesInsightUnavailable>
{
    public const string Kind = "sales-insight-projection";

    private static readonly IReadOnlyList<SalesInsightDisplay> Displays =
        Array.AsReadOnly([SalesInsightDisplay.BarChart, SalesInsightDisplay.Table]);

    private static readonly IReadOnlyList<SalesInsightPlacement> Placements =
        Array.AsReadOnly([SalesInsightPlacement.Chat, SalesInsightPlacement.ContextDrawer]);

    public Task HandleAsync(SalesInsightReady synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var result = synapse.Result;
        if (!MatchesOrigin(result.Query.QueryId) || State.SurfaceProduced)
        {
            return Task.CompletedTask;
        }

        var state = State;
        state.SurfaceProduced = true;
        State = state;
        Emit(new SalesInsightSurfaceRequested(
            result.Query.QueryId,
            result.Query.Range,
            result.Query.CurrencyCode,
            result.Buckets,
            result.TotalAmount,
            result.ClosedDealCount,
            result.Context,
            Displays,
            Placements));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesInsightUnavailable synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesOrigin(synapse.QueryId) || State.SurfaceProduced)
        {
            return Task.CompletedTask;
        }

        var state = State;
        state.SurfaceProduced = true;
        State = state;
        Emit(new SalesInsightUnavailableSurfaceRequested(
            synapse.QueryId,
            synapse.Context,
            synapse.Reason,
            Placements));
        return Task.CompletedTask;
    }

    private bool MatchesOrigin(string queryId)
        => string.Equals(Id.Name, queryId, StringComparison.Ordinal)
            && Origin.Source == new NeuronId(SalesInsightNeuron.Kind, queryId);
}
