namespace DigitalBrain.Mocks;

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
