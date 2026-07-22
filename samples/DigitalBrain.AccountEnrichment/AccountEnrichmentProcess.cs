using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;

namespace DigitalBrain.AccountEnrichment;

public sealed class AccountEnrichmentProcess : Neuron,
    IHandle<EnrichAccountFromEmail>,
    IHandle<ApproveAccountEnrichment>,
    IEmit<AccountEnrichmentProposed>,
    IEmit<AccountEnriched>
{
    public async Task HandleAsync(
        EnrichAccountFromEmail synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var gmail = GrainFactory.GetGrain<IGmail>(
            NeuronId.For<IGmail>(Id.Owner, "gmail").ToGrainId());
        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var message = await gmail.ReadMessageAsync(synapse.MessageId);
        var description = $"Email from {message.Sender}: {message.Subject}\n{message.PlaintextBody}";
        var mutation = await salesforce.ProposeAccountDescriptionAsync(
            synapse.CommandId,
            synapse.AccountId,
            description);

        await EmitAsync(new AccountEnrichmentProposed(
            synapse.CommandId,
            synapse.MessageId,
            synapse.AccountId,
            description,
            mutation.Fingerprint));
    }

    public async Task HandleAsync(
        ApproveAccountEnrichment synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var mutation = await salesforce.ApproveAccountDescriptionAsync(
            synapse.CommandId,
            synapse.Fingerprint);

        if (mutation.State is not SalesforceMutationState.Completed)
        {
            throw new InvalidOperationException(
                $"Salesforce could not prove completion of Account '{mutation.AccountId}' enrichment.");
        }

        await EmitAsync(new AccountEnriched(
            mutation.CommandId,
            synapse.MessageId,
            mutation.AccountId,
            mutation.Description));
    }
}
