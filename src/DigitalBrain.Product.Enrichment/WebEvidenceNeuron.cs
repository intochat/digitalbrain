using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Runs the web-research provider boundary for one enrichment run.
/// </summary>
public sealed class WebEvidenceNeuron(IWebEvidenceResearcher researcher) : Neuron, INeuron<WebEvidenceRequested>
{
    public const string Kind = "account-enrichment-web-evidence";

    private readonly IWebEvidenceResearcher researcher = researcher ?? throw new ArgumentNullException(nameof(researcher));

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Provider failure is deliberately converted to a redacted product availability fact.")]
    public async Task HandleAsync(WebEvidenceRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesRun(synapse.Request)
            || !Equals(Origin.Source, new NeuronId(AccountEnrichmentNeuron.Kind, synapse.Request.RunId)))
        {
            return;
        }

        try
        {
            var evidence = await researcher.ResearchAsync(new WebEvidenceRequest(synapse.Request), cancellationToken);
            if (evidence is not { Count: > 0 })
            {
                Emit(
                    new WebEvidenceUnavailable(synapse.Request.RunId),
                    Dispatch.Direct(new NeuronId(AccountEnrichmentNeuron.Kind, synapse.Request.RunId)));
                return;
            }

            Emit(
                new WebEvidenceCollected(synapse.Request.RunId, evidence),
                Dispatch.Direct(new NeuronId(AccountEnrichmentNeuron.Kind, synapse.Request.RunId)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Emit(
                new WebEvidenceUnavailable(synapse.Request.RunId),
                Dispatch.Direct(new NeuronId(AccountEnrichmentNeuron.Kind, synapse.Request.RunId)));
        }
    }

    private bool MatchesRun(AccountEnrichmentRequest request)
        => string.Equals(Id.Name, request.RunId, StringComparison.Ordinal);
}
