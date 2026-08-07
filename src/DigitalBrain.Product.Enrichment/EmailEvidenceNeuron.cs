using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Product.Enrichment;

/// <summary>
/// Runs the email-provider boundary for one enrichment run.
/// </summary>
public sealed class EmailEvidenceNeuron(IEmailEvidenceReader reader) : Neuron, INeuron<EmailEvidenceRequested>
{
    public const string Kind = "account-enrichment-email-evidence";

    private readonly IEmailEvidenceReader reader = reader ?? throw new ArgumentNullException(nameof(reader));

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Provider failure is deliberately converted to a redacted product availability fact.")]
    public async Task HandleAsync(EmailEvidenceRequested synapse, CancellationToken cancellationToken)
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
            var evidence = await reader.ReadAsync(new EmailEvidenceRequest(synapse.Request), cancellationToken);
            if (evidence is not { Count: > 0 })
            {
                Emit(
                    new EmailEvidenceUnavailable(synapse.Request.RunId),
                    Dispatch.Direct(new NeuronId(AccountEnrichmentNeuron.Kind, synapse.Request.RunId)));
                return;
            }

            Emit(
                new EmailEvidenceCollected(synapse.Request.RunId, evidence),
                Dispatch.Direct(new NeuronId(AccountEnrichmentNeuron.Kind, synapse.Request.RunId)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Emit(
                new EmailEvidenceUnavailable(synapse.Request.RunId),
                Dispatch.Direct(new NeuronId(AccountEnrichmentNeuron.Kind, synapse.Request.RunId)));
        }
    }

    private bool MatchesRun(AccountEnrichmentRequest request)
        => string.Equals(Id.Name, request.RunId, StringComparison.Ordinal);
}
