using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Core;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.AccountEnrichment;

internal sealed class AccountEnrichment :
    Neuron,
    IAccountEnrichment,
    IHandle<EnrichAccountFromEmail>,
    IHandle<GmailResponse>,
    IHandle<SalesforceResponse>,
    IHandle<SalesforceMutationApproval>,
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

        Stage(
            synapse.CommandId,
            new Request(
                synapse.MessageId,
                synapse.GmailAccount,
                synapse.AccountId,
                Description: string.Empty,
                MutationFingerprint: string.Empty,
                Completed: false,
                Phase: RequestPhase.ReadingGmail));

        await SendAsync(
            NeuronId.For<IGmail>(Id.Owner, synapse.GmailAccount),
            new GmailRequest($"Read Gmail message {synapse.MessageId}", synapse.CommandId));
    }

    public async Task HandleAsync(GmailResponse synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var request = Load(synapse.CommandId)
            ?? throw new InvalidOperationException($"Account enrichment '{synapse.CommandId}' has no durable request.");
        if (request.Completed || request.Phase is not RequestPhase.ReadingGmail)
        {
            return;
        }

        if (!synapse.Succeeded)
        {
            throw new InvalidOperationException(synapse.Error ?? "Gmail intent failed.");
        }

        var message = SelectMessage(synapse, request.MessageId);
        var description =
            $"Email from {message.Sender}: {message.Subject}\n{message.PlaintextBody}";

        Stage(
            synapse.CommandId,
            request with
            {
                Description = description,
                Phase = RequestPhase.ProposingSalesforce,
            });

        await SendAsync(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce"),
            new SalesforceRequest(
                $"Propose Account Description for {request.AccountId}",
                synapse.CommandId,
                request.AccountId,
                description));
    }

    public async Task HandleAsync(SalesforceResponse synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var request = Load(synapse.CommandId)
            ?? throw new InvalidOperationException($"Account enrichment '{synapse.CommandId}' has no durable request.");
        if (request.Completed || request.Phase is not RequestPhase.ProposingSalesforce)
        {
            return;
        }

        if (!synapse.Succeeded || synapse.Mutation is null)
        {
            throw new InvalidOperationException(synapse.Error ?? "Salesforce propose failed.");
        }

        Stage(
            synapse.CommandId,
            request with
            {
                MutationFingerprint = synapse.Mutation.Fingerprint,
                Phase = RequestPhase.AwaitingApproval,
            });

        await EmitAsync(new AccountEnrichmentProposed(
            synapse.CommandId,
            request.MessageId,
            request.AccountId,
            request.Description,
            synapse.Mutation.Fingerprint));
    }

    public async Task HandleAsync(SalesforceMutationApproval synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var request = Load(synapse.CommandId)
            ?? throw new InvalidOperationException($"Account enrichment '{synapse.CommandId}' has no durable request.");
        if (request.Completed)
        {
            return;
        }

        if (!string.Equals(request.MutationFingerprint, synapse.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Salesforce approval '{synapse.ApprovalId}' does not match the enrichment proposal.");
        }

        Stage(synapse.CommandId, request with { Completed = true, Phase = RequestPhase.Completed });
        await EmitAsync(new AccountEnriched(
            synapse.CommandId,
            request.MessageId,
            request.AccountId,
            request.Description));
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

    private static GmailMessage SelectMessage(GmailResponse mail, string messageId)
    {
        for (var index = 0; index < mail.Messages.Count; index++)
        {
            if (string.Equals(mail.Messages[index].Id, messageId, StringComparison.Ordinal))
            {
                return mail.Messages[index];
            }
        }

        if (mail.Messages.Count > 0)
        {
            return mail.Messages[0];
        }

        throw new InvalidOperationException($"Gmail returned no message for '{messageId}'.");
    }

    [GenerateSerializer]
    internal sealed record Request(
        [property: Id(0)] string MessageId,
        [property: Id(1)] string GmailAccount,
        [property: Id(2)] string AccountId,
        [property: Id(3)] string Description,
        [property: Id(4)] string MutationFingerprint,
        [property: Id(5)] bool Completed,
        [property: Id(6)] RequestPhase Phase);

    internal enum RequestPhase
    {
        ReadingGmail = 0,
        ProposingSalesforce = 1,
        AwaitingApproval = 2,
        Completed = 3,
    }
}
