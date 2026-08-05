using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

// Orchestrates inbound email → mock web research → Salesforce propose (+ optional enrich).
// Ambient CRM facts need a catalog listener so Emit is legal (S02 trap).
public sealed class AccountEnricher : Neuron, INeuron<EmailReceived>, INeuron<WebSearchCompleted>
{
    public Task HandleAsync(EmailReceived email, CancellationToken cancellationToken)
    {
        Ask<WebSearchCompleted>(new WebSearchRequested(
            Query: $"company profile {email.Domain}",
            Domain: email.Domain));
        return Task.CompletedTask;
    }

    public Task HandleAsync(WebSearchCompleted research, CancellationToken cancellationToken)
    {
        var fieldDiff =
            $"industry=technology;headline={research.Snippet};source={research.Source}";
        Emit(new ProposeAccountEnrichment(
            AccountId: null,
            Domain: research.Domain,
            FieldDiff: fieldDiff,
            Confidence: 0.84));
        return Task.CompletedTask;
    }
}

// Catalog sink for ambient CRM proposals/completions — proves declared fan-out without reentering the enricher.
public sealed class EnrichmentDesk : Neuron,
    INeuron<AccountEnrichmentProposed>,
    INeuron<AccountEnriched>
{
    public Task HandleAsync(AccountEnrichmentProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(AccountEnriched fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
