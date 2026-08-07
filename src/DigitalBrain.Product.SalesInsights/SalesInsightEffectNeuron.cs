using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Product.SalesInsights;

/// <summary>
/// Owns the read-side provider call and returns a redacted typed outcome to
/// the matching Sales Insights state behavior.
/// </summary>
public sealed class SalesInsightEffectNeuron(ISalesRevenueReader reader) : Neuron,
    INeuron<SalesRevenueReadRequested>
{
    public const string Kind = "sales-insight-effect";

    private readonly ISalesRevenueReader reader = reader ?? throw new ArgumentNullException(nameof(reader));

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Provider failures deliberately become a redacted unavailable product outcome.")]
    public async Task HandleAsync(SalesRevenueReadRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(Id.Name, synapse.Query.QueryId, StringComparison.Ordinal)
            || Origin.Source != new NeuronId(SalesInsightNeuron.Kind, synapse.Query.QueryId))
        {
            return;
        }

        try
        {
            var records = await reader.ReadClosedWonAsync(synapse.Query, cancellationToken);
            if (records is null || records.Count > SalesInsightLimits.MaximumReaderRecords)
            {
                Emit(
                    new SalesRevenueReadUnavailable(
                        synapse.Query.QueryId,
                        SalesInsightUnavailableReason.InvalidReaderData),
                    Dispatch.Direct(new NeuronId(SalesInsightNeuron.Kind, synapse.Query.QueryId)));
                return;
            }

            Emit(
                new SalesRevenueReadCompleted(synapse.Query, records),
                Dispatch.Direct(new NeuronId(SalesInsightNeuron.Kind, synapse.Query.QueryId)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Emit(
                new SalesRevenueReadUnavailable(
                    synapse.Query.QueryId,
                    SalesInsightUnavailableReason.ReaderUnavailable),
                Dispatch.Direct(new NeuronId(SalesInsightNeuron.Kind, synapse.Query.QueryId)));
        }
    }
}
