using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.AccountEnrichment;

internal sealed class AccountEnrichment :
    Neuron,
    IAccountEnrichment,
    IHandle<EnrichAccountFromEmail>,
    IHandle<SalesforceMutationApproval>,
    IHandle<ExecuteApprovedAccountEnrichment>,
    IEmit<AccountEnrichmentProposed>,
    IEmit<AccountEnriched>
{
    private const string RequestsName = "account-enrichment.requests";
    private readonly IDurableDictionary<Guid, byte[]> _requests;
    private readonly Serializer<Request> _states;

    public AccountEnrichment()
    {
        _requests = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(RequestsName);
        _states = ServiceProvider.GetRequiredService<Serializer<Request>>();
    }

    public async Task HandleAsync(EnrichAccountFromEmail synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        Validate(synapse);

        if (Load(synapse.CommandId) is { } existing)
        {
            EnsureSame(existing, synapse);
            return;
        }

        var gmail = GrainFactory.GetGrain<IGmail>(NeuronId.For<IGmail>(Id.Owner, synapse.GmailAccount).ToGrainId());
        var salesforce = GrainFactory.GetGrain<ISalesforce>(NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());

        var message = await gmail.ReadMessage(synapse.CommandId, synapse.MessageId, cancellationToken);
        var description =
            $"Email from {message.Sender}: {message.Subject}\n{message.PlaintextBody}";
        var mutation = await salesforce.ProposeAccountDescription(
            synapse.CommandId,
            Id,
            synapse.AccountId,
            description,
            cancellationToken);

        Stage(
            synapse.CommandId,
            new Request(
                synapse.MessageId,
                synapse.GmailAccount,
                synapse.AccountId,
                description,
                mutation.Fingerprint,
                Completed: false));

        await EmitAsync(new AccountEnrichmentProposed(
            synapse.CommandId,
            synapse.MessageId,
            synapse.AccountId,
            description,
            mutation.Fingerprint));
    }

    public Task HandleAsync(SalesforceMutationApproval synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var request = Load(synapse.CommandId)
            ?? throw new InvalidOperationException($"Account enrichment '{synapse.CommandId}' has no durable request.");
        if (request.Completed)
        {
            return Task.CompletedTask;
        }

        if (!string.Equals(request.MutationFingerprint, synapse.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Salesforce approval '{synapse.ApprovalId}' does not match the enrichment proposal.");
        }

        return SendAsync(Id, new ExecuteApprovedAccountEnrichment(synapse));
    }

    public async Task HandleAsync(ExecuteApprovedAccountEnrichment synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        var approval = synapse.Approval;

        var request = Load(approval.CommandId)
            ?? throw new InvalidOperationException($"Account enrichment '{approval.CommandId}' has no durable request.");
        if (request.Completed)
        {
            return;
        }

        if (!string.Equals(request.MutationFingerprint, approval.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Salesforce approval '{approval.ApprovalId}' does not match the enrichment proposal.");
        }

        var evidence = await ApprovalEvidenceAsync(approval);
        var salesforce = GrainFactory.GetGrain<ISalesforce>(NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var mutation = await salesforce.ApproveAccountDescription(approval, evidence, cancellationToken);

        if (mutation.State is not SalesforceMutationState.Completed)
        {
            throw new InvalidOperationException(
                $"Salesforce could not prove completion of Account '{mutation.AccountId}' enrichment.");
        }

        Stage(approval.CommandId, request with { Completed = true });
        await EmitAsync(new AccountEnriched(
            mutation.CommandId,
            request.MessageId,
            mutation.AccountId,
            mutation.Description));
    }

    Task INeuron.Deliver(SynapseDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        if (delivery.Synapse is SalesforceMutationApproval approval
            && (delivery.Caller != approval.Approver
                || approval.Approver.Type != ISessionNeuron.GrainTypeName
                || approval.Approver.Owner != Id.Owner))
        {
            return Task.CompletedTask;
        }

        return base.Deliver(delivery, cancellationToken);
    }

    private async Task<SynapseDelivery> ApprovalEvidenceAsync(SalesforceMutationApproval approval)
    {
        var incoming = await ReadJournal(JournalKind.Incoming, afterSequence: 0);
        return incoming.Delta.FirstOrDefault(delivery =>
                delivery.Caller == approval.Approver
                && delivery.Synapse is SalesforceMutationApproval recorded
                && recorded == approval)
            ?? throw new InvalidOperationException(
                $"Salesforce approval '{approval.ApprovalId}' has no durable human delivery evidence.");
    }

    private Request? Load(CommandId commandId)
        => _requests.TryGetValue(commandId.Value, out var serialized)
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(CommandId commandId, Request data)
    {
        var key = commandId.Value;
        var existed = _requests.TryGetValue(key, out var previous);
        EnlistTurnRollback(() =>
        {
            if (existed)
            {
                _requests[key] = previous!;
            }
            else
            {
                _requests.Remove(key);
            }
        });
        _requests[key] = _states.SerializeToArray(data);
    }

    private static void EnsureSame(Request existing, EnrichAccountFromEmail synapse)
    {
        if (!string.Equals(existing.MessageId, synapse.MessageId, StringComparison.Ordinal)
            || !string.Equals(existing.GmailAccount, synapse.GmailAccount, StringComparison.Ordinal)
            || !string.Equals(existing.AccountId, synapse.AccountId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An account-enrichment command id cannot be reused with different input.");
        }
    }

    private static void Validate(EnrichAccountFromEmail request)
    {
        if (request.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException("An account-enrichment command id cannot be empty.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.MessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GmailAccount);
    }

    [GenerateSerializer]
    internal sealed record Request(
        [property: Id(0)] string MessageId,
        [property: Id(1)] string GmailAccount,
        [property: Id(2)] string AccountId,
        [property: Id(3)] string Description,
        [property: Id(4)] string MutationFingerprint,
        [property: Id(5)] bool Completed);
}
