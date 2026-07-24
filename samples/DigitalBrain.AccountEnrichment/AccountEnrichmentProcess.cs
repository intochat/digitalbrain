using System.Security.Cryptography;
using System.Text.Json;
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
    IHandle<ContinueAccountEnrichment>,
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
        Validate(synapse);
        _ = GmailId(synapse.GmailAccount);

        var fingerprint = Fingerprint(synapse);
        var existing = Load(synapse.CommandId);

        if (existing is not null)
        {
            EnsureSame(existing, fingerprint);
            return;
        }

        Stage(
            synapse.CommandId,
            new AccountEnrichmentData(
                synapse.MessageId,
                synapse.GmailAccount,
                synapse.AccountId,
                fingerprint,
                Description: null,
                MutationFingerprint: null,
                AccountEnrichmentPhase.Fenced));
        await SendAsync(Id, new ContinueAccountEnrichment(synapse.CommandId));
    }

    async Task IHandle<ContinueAccountEnrichment>.HandleAsync(
        ContinueAccountEnrichment synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var request = Load(synapse.CommandId)
            ?? throw new InvalidOperationException(
                $"Account enrichment '{synapse.CommandId}' has no durable request fence.");
        EnsureIntact(synapse.CommandId, request);

        if (request.Phase is AccountEnrichmentPhase.Proposed or AccountEnrichmentPhase.Completed)
        {
            return;
        }

        if (request.Phase is not AccountEnrichmentPhase.Fenced)
        {
            throw new InvalidOperationException(
                $"Account enrichment '{synapse.CommandId}' cannot continue from {request.Phase}.");
        }

        var gmail = GrainFactory.GetGrain<IGmail>(
            GmailId(request.GmailAccount).ToGrainId());
        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var message = await gmail.ReadMessage(request.MessageId, cancellationToken);
        var description = $"Email from {message.Sender}: {message.Subject}\n{message.PlaintextBody}";
        var mutation = await salesforce.ProposeAccountDescription(
            synapse.CommandId,
            Id,
            request.AccountId,
            description,
            cancellationToken);

        Stage(
            synapse.CommandId,
            request with
            {
                Description = description,
                MutationFingerprint = mutation.Fingerprint,
                Phase = AccountEnrichmentPhase.Proposed,
            });
        await EmitAsync(new AccountEnrichmentProposed(
            synapse.CommandId,
            request.MessageId,
            request.AccountId,
            description,
            mutation.Fingerprint));
    }

    public async Task HandleAsync(
        SalesforceMutationApproval synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var request = Load(synapse.CommandId)
            ?? throw new InvalidOperationException(
                $"Account enrichment '{synapse.CommandId}' has no durable request context.");
        EnsureIntact(synapse.CommandId, request);
        EnsureApproval(request, synapse);

        if (request.Phase is AccountEnrichmentPhase.Completed)
        {
            return;
        }

        if (request.Phase is not AccountEnrichmentPhase.Proposed)
        {
            throw new InvalidOperationException(
                $"Account enrichment '{synapse.CommandId}' cannot be approved from {request.Phase}.");
        }

        await SendAsync(Id, new ExecuteApprovedAccountEnrichment(synapse));
    }

    async Task IHandle<ExecuteApprovedAccountEnrichment>.HandleAsync(
        ExecuteApprovedAccountEnrichment synapse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var request = Load(synapse.Approval.CommandId)
            ?? throw new InvalidOperationException(
                $"Account enrichment '{synapse.Approval.CommandId}' has no durable request context.");
        EnsureIntact(synapse.Approval.CommandId, request);
        EnsureApproval(request, synapse.Approval);

        if (request.Phase is AccountEnrichmentPhase.Completed)
        {
            return;
        }

        if (request.Phase is not AccountEnrichmentPhase.Proposed)
        {
            throw new InvalidOperationException(
                $"Account enrichment '{synapse.Approval.CommandId}' cannot complete from {request.Phase}.");
        }

        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var evidence = await FindApprovalEvidenceAsync(synapse.Approval);
        var mutation = await salesforce.ApproveAccountDescription(
            synapse.Approval,
            evidence,
            cancellationToken);

        if (mutation.State is not SalesforceMutationState.Completed)
        {
            throw new InvalidOperationException(
                $"Salesforce could not prove completion of Account '{mutation.AccountId}' enrichment.");
        }

        if (mutation.CommandId != synapse.Approval.CommandId
            || !string.Equals(mutation.AccountId, request.AccountId, StringComparison.Ordinal)
            || !string.Equals(mutation.Description, request.Description, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Salesforce returned a receipt that does not match account enrichment '{synapse.Approval.CommandId}'.");
        }

        Stage(
            mutation.CommandId,
            request with { Phase = AccountEnrichmentPhase.Completed });
        await EmitAsync(new AccountEnriched(
            mutation.CommandId,
            request.MessageId,
            mutation.AccountId,
            mutation.Description));
    }

    Task INeuron.Deliver(SynapseDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return delivery.Synapse is SalesforceMutationApproval approval
            && (delivery.Caller != approval.Approver
                || approval.Approver.Type != ISessionNeuron.GrainTypeName
                || approval.Approver.Owner != Id.Owner)
            ? Task.CompletedTask
            : base.Deliver(delivery);
    }

    private async Task<SynapseDelivery> FindApprovalEvidenceAsync(
        SalesforceMutationApproval approval)
    {
        var incoming = await ReadJournal(JournalKind.Incoming, afterSequence: 0);
        var evidence = incoming.Delta.FirstOrDefault(delivery =>
            delivery.Caller == approval.Approver
            && delivery.Synapse is SalesforceMutationApproval recorded
            && recorded == approval);

        return evidence
            ?? throw new InvalidOperationException(
                $"Salesforce approval '{approval.ApprovalId}' has no durable human delivery evidence.");
    }

    private AccountEnrichmentData? Load(CommandId commandId)
        => _requests.TryGetValue(commandId.Value, out var serialized)
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(CommandId commandId, AccountEnrichmentData data)
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

    private static void EnsureApproval(
        AccountEnrichmentData request,
        SalesforceMutationApproval approval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approval.Fingerprint);

        if (!string.Equals(
            request.MutationFingerprint,
            approval.Fingerprint,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Salesforce approval '{approval.ApprovalId}' does not match its account-enrichment proposal.");
        }
    }

    private static void EnsureIntact(
        CommandId commandId,
        AccountEnrichmentData request)
        => EnsureSame(
            request,
            Fingerprint(
                commandId,
                request.GmailAccount,
                request.MessageId,
                request.AccountId));

    private static void EnsureSame(
        AccountEnrichmentData request,
        string fingerprint)
    {
        if (!string.Equals(
            request.RequestFingerprint,
            fingerprint,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An account-enrichment command id cannot be reused with different input.");
        }
    }

    private static string Fingerprint(EnrichAccountFromEmail request)
        => Fingerprint(
            request.CommandId,
            request.GmailAccount,
            request.MessageId,
            request.AccountId);

    private static string Fingerprint(
        CommandId commandId,
        string gmailAccount,
        string messageId,
        string accountId)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[]
            {
                commandId.Value.ToString("N"),
                gmailAccount,
                messageId,
                accountId,
            })));

    private NeuronId GmailId(string account)
        => NeuronId.For<IGmail>(Id.Owner, account);

    private static void Validate(EnrichAccountFromEmail request)
    {
        if (request.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "An account-enrichment command id cannot be empty.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.MessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccountId);
    }

    [GenerateSerializer]
    internal sealed record AccountEnrichmentData(
        [property: Id(0)] string MessageId,
        [property: Id(1)] string GmailAccount,
        [property: Id(2)] string AccountId,
        [property: Id(3)] string RequestFingerprint,
        [property: Id(4)] string? Description,
        [property: Id(5)] string? MutationFingerprint,
        [property: Id(6)] AccountEnrichmentPhase Phase);

    internal enum AccountEnrichmentPhase
    {
        Fenced,
        Proposed,
        Completed,
    }
}
