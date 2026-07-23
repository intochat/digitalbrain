using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.AccountEnrichment;

public sealed class AccountEnrichmentProcess : Neuron,
    INeuron,
    IHandle<EnrichAccountFromEmail>,
    IHandle<SalesforceMutationApproval>,
    IHandle<ExecuteApprovedAccountEnrichment>,
    IEmit<AccountEnrichmentProposed>,
    IEmit<AccountEnriched>
{
    private const string RequestsName = "account-enrichment.requests";
    private readonly IDurableDictionary<Guid, byte[]> _requests;
    private readonly Serializer<AccountEnrichmentData> _states;

    public AccountEnrichmentProcess()
    {
        _requests = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(
            RequestsName);
        _states = ServiceProvider.GetRequiredService<Serializer<AccountEnrichmentData>>();
    }

    public async Task HandleAsync(
        EnrichAccountFromEmail synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var gmail = GrainFactory.GetGrain<IGmail>(
            NeuronId.For<IGmail>(Id.Owner, "gmail").ToGrainId());
        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var message = await gmail.ReadMessageAsync(synapse.MessageId, cancellationToken);
        var description = $"Email from {message.Sender}: {message.Subject}\n{message.PlaintextBody}";
        var mutation = await salesforce.ProposeAccountDescriptionAsync(
            synapse.CommandId,
            Id,
            synapse.AccountId,
            description,
            cancellationToken);

        _requests[synapse.CommandId.Value] = _states.SerializeToArray(
            new AccountEnrichmentData(synapse.MessageId));
        await WriteStateAsync(cancellationToken);

        await EmitAsync(new AccountEnrichmentProposed(
            synapse.CommandId,
            synapse.MessageId,
            synapse.AccountId,
            description,
            mutation.Fingerprint));
    }

    public async Task HandleAsync(
        SalesforceMutationApproval synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        await SendAsync(Id, new ExecuteApprovedAccountEnrichment(synapse));
    }

    async Task IHandle<ExecuteApprovedAccountEnrichment>.HandleAsync(
        ExecuteApprovedAccountEnrichment synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var evidence = await FindApprovalEvidenceAsync(synapse.Approval);
        var mutation = await salesforce.ApproveAccountDescriptionAsync(
            synapse.Approval,
            evidence,
            cancellationToken);

        if (mutation.State is not SalesforceMutationState.Completed)
        {
            throw new InvalidOperationException(
                $"Salesforce could not prove completion of Account '{mutation.AccountId}' enrichment.");
        }

        if (!_requests.TryGetValue(mutation.CommandId.Value, out var serialized))
        {
            throw new InvalidOperationException(
                $"Account enrichment '{mutation.CommandId}' has no durable request context.");
        }

        var request = _states.Deserialize(serialized);
        await EmitAsync(new AccountEnriched(
            mutation.CommandId,
            request.MessageId,
            mutation.AccountId,
            mutation.Description));
    }

    Task INeuron.DeliverAsync(SynapseDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return delivery.Synapse is SalesforceMutationApproval approval
            && (delivery.Caller != approval.Approver
                || approval.Approver.Type != ISessionNeuron.GrainTypeName
                || approval.Approver.Owner != Id.Owner)
            ? Task.CompletedTask
            : base.DeliverAsync(delivery);
    }

    private async Task<SynapseDelivery> FindApprovalEvidenceAsync(
        SalesforceMutationApproval approval)
    {
        var incoming = await ReadJournalAsync(JournalKind.Incoming, afterSequence: 0);
        var evidence = incoming.Delta.FirstOrDefault(delivery =>
            delivery.Caller == approval.Approver
            && delivery.Synapse is SalesforceMutationApproval recorded
            && recorded == approval);

        return evidence
            ?? throw new InvalidOperationException(
                $"Salesforce approval '{approval.ApprovalId}' has no durable human delivery evidence.");
    }

    [GenerateSerializer]
    internal sealed record AccountEnrichmentData(
        [property: Id(0)] string MessageId);
}
