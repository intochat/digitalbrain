namespace DigitalBrain.Mocks;

public sealed record ProposeAccountEnrichment(
    string? AccountId,
    string Domain,
    string FieldDiff,
    double Confidence) : Synapse;

public sealed record AccountEnrichmentProposed(
    string? AccountId,
    string Domain,
    string FieldDiff,
    double Confidence) : Synapse;

// Optional terminal CRM write fact — same turn as the proposal so approval UI is not required for S01.
public sealed record AccountEnriched(
    string? AccountId,
    string Domain,
    string FieldDiff) : Synapse;

[GrainType("mocksalesforce")]
public sealed class MockSalesforce : Neuron, INeuron<ProposeAccountEnrichment>
{
    public Task HandleAsync(ProposeAccountEnrichment command, CancellationToken cancellationToken)
    {
        Emit(new AccountEnrichmentProposed(
            command.AccountId,
            command.Domain,
            command.FieldDiff,
            command.Confidence));
        Emit(new AccountEnriched(
            command.AccountId,
            command.Domain,
            command.FieldDiff));
        return Task.CompletedTask;
    }
}
