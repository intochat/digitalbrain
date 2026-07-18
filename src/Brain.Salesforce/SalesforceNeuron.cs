using System.Diagnostics;
using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Streams;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceNeuron(
    [FromKeyedServices("salesforce-receipts")] IDurableDictionary<string, CommandReceipt> receipts,
    [FromKeyedServices("salesforce-events")] IDurableDictionary<string, byte> processedEvents,
    [FromKeyedServices("salesforce-sequences")] IDurableDictionary<string, long> sourceSequences,
    [FromKeyedServices("salesforce-outbox")] IDurableList<OutboxIntent<SalesforceFeedEvent>> outbox,
    [FromKeyedServices("salesforce-domain")] IDurableDictionary<string, string> domain,
    [FromKeyedServices("salesforce-flags")] IDurableDictionary<string, string> flags,
    [FromKeyedServices("salesforce-failures")] IDurableList<SanitizedFailure> failures,
    [FromKeyedServices("salesforce-accepted-causation")] IDurableDictionary<string, byte> acceptedCausation,
    [FromKeyedServices("salesforce-rejected-causation")] IDurableDictionary<string, byte> rejectedCausation,
    ISalesforceMcpClient mcpClient) : ReactiveNeuron<SalesforceFeedEvent>(
        receipts,
        processedEvents,
        sourceSequences,
        outbox,
        domain,
        flags,
        failures,
        acceptedCausation,
        rejectedCausation), ISalesforce, ISalesforceNeuronControl
{
    public static readonly ActivitySource ActivitySource = new("DigitalBrain.Salesforce");

    private readonly ISalesforceMcpClient _mcpClient = mcpClient;
    private Guid _activationToken;

    private string NeuronSurfaceId => this.GetPrimaryKeyString();
    private Guid NeuronFeedStreamId => SalesforceConstants.FeedStreamIdFor(NeuronSurfaceId);

    protected override bool AutoDrainAfterCommit =>
        !Flags.TryGetValue(SalesforceConstants.AutoDrainFlag, out var value) || value != "0";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationToken = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());

    public Task<CommandReceipt> QueryRecordsAsync(CommandSynapse<SalesforceQueryRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            using var activity = ActivitySource.StartActivity("salesforce.query");
            activity?.SetTag("salesforce.soqlLength", payload.Soql.Length);

            var result = await _mcpClient.QueryRecordsAsync(payload.Soql);
            var surfaceText = $"records:{result.RecordCount}";
            Flags[SalesforceConstants.SurfaceTextFlag] = surfaceText;
            var nextUi = UiRevision + 1;
            var candidate = CreateSnapshotCandidate(surfaceText, nextUi);
            var intent = CreateFeedIntent(
                command.Metadata,
                SalesforceFeedEvent.UiSurface(surfaceText, candidate),
                SalesforceConstants.OutcomeEventId(command.Metadata.CommandId, SalesforceFeedEvent.UiSurfaceKind));
            await commit(new ReactiveCommit<SalesforceFeedEvent>(surfaceText, nextUi, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> UpdateRecordAsync(CommandSynapse<SalesforceUpdateRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            using var activity = ActivitySource.StartActivity("salesforce.update");
            activity?.SetTag("salesforce.objectTypeLength", payload.ObjectType.Length);
            activity?.SetTag("salesforce.fieldCount", payload.Fields.Count);

            var idempotencyKey = command.Metadata.CommandId.ToString("N");
            if (IsTerminal(idempotencyKey))
            {
                var existing = ResolveSurfaceText();
                await commit(new ReactiveCommit<SalesforceFeedEvent>(existing, UiRevision, []));
                return CommandReceiptStatus.Accepted;
            }

            var effectId = command.Metadata.CommandId;
            var surfaceText = "update-pending";
            Flags[SalesforceConstants.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(
                command.Metadata,
                SalesforceFeedEvent.UpdateEffect(
                    effectId,
                    idempotencyKey,
                    payload.ObjectType,
                    payload.RecordId,
                    payload.Fields),
                effectId);
            await commit(new ReactiveCommit<SalesforceFeedEvent>(surfaceText, UiRevision + 1, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<UiSurfaceSnapshot> GetSurfaceAsync()
    {
        var text = ResolveSurfaceText();
        return Task.FromResult(new UiSurfaceSnapshot(new UiSurface(
            NeuronSurfaceId,
            UiRevision,
            [new UiBlock("text", text, [])])));
    }

    public async Task SetAutoDrainAsync(bool enabled)
    {
        Flags[SalesforceConstants.AutoDrainFlag] = enabled ? "1" : "0";
        await WriteStateAsync(CancellationToken.None);
    }

    public Task DrainOutboxAsync() => DrainOutboxCoreAsync(throwOnPublishFailure: false);

    public Task DrainOutboxStrictAsync() => DrainOutboxCoreAsync(throwOnPublishFailure: true);

    public Task<int> GetOutboxCountAsync() => Task.FromResult(Outbox.Count);

    public Task<OutboxIntent<SalesforceFeedEvent>?> PeekOutboxAsync() =>
        Task.FromResult(Outbox.Count == 0 ? null : Outbox[0]);

    public Task ReplayOutboxIntentAsync(OutboxIntent<SalesforceFeedEvent> intent) =>
        PublishOutboxIntentAsync(intent);

    public Task<SanitizedFailure?> GetLastFailureAsync() =>
        Task.FromResult(Failures.Count == 0 ? null : Failures[^1]);

    public Task<Guid> GetActivationTokenAsync() => Task.FromResult(_activationToken);

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task<Guid> GetFeedStreamIdAsync() => Task.FromResult(NeuronFeedStreamId);

    public async Task SetFailNextOutcomePublishAsync(int count)
    {
        Flags[SalesforceConstants.FailNextOutcomePublishFlag] = count.ToString();
        await WriteStateAsync(CancellationToken.None);
    }

    public Task<bool> HasEffectTerminalAsync(string idempotencyKey) =>
        Task.FromResult(IsTerminal(idempotencyKey));

    protected override async Task PublishOutboxIntentAsync(OutboxIntent<SalesforceFeedEvent> intent)
    {
        var payload = intent.Event.Payload;
        if (payload.Kind is SalesforceFeedEvent.UiSurfaceKind
            or SalesforceFeedEvent.UpdateCompletedKind
            or SalesforceFeedEvent.UpdateFailedKind)
        {
            await PublishOutcomeWithSeamAsync(intent);
            return;
        }

        if (payload.Kind != SalesforceFeedEvent.UpdateEffectKind)
        {
            await base.PublishOutboxIntentAsync(intent);
            return;
        }

        var doneKey = SalesforceConstants.EffectDoneFlagPrefix + payload.IdempotencyKey;
        var failedKey = SalesforceConstants.EffectFailedFlagPrefix + payload.IdempotencyKey;
        if (Flags.ContainsKey(doneKey) || Flags.ContainsKey(failedKey))
            return;

        SalesforceUpdateResult result;
        try
        {
            result = await _mcpClient.UpdateRecordAsync(
                payload.ObjectType,
                payload.RecordId,
                payload.Fields,
                payload.IdempotencyKey);
        }
        catch (Exception)
        {
            await JournalTerminalFailureAsync(intent, payload);
            return;
        }

        const string completedSurface = "update-completed";
        Flags[doneKey] = "1";
        Flags[SalesforceConstants.SurfaceTextFlag] = completedSurface;
        var nextUi = UiRevision + 1;
        Flags[ReactiveNeuronPipeline<SalesforceFeedEvent>.UiRevisionKey] = nextUi.ToString();
        Flags[ReactiveNeuronPipeline<SalesforceFeedEvent>.RevisionKey] = (CurrentRevision + 1).ToString();

        var candidate = CreateSnapshotCandidate(completedSurface, nextUi);
        var outcome = CreateFeedIntent(
            intent.Event.Metadata,
            SalesforceFeedEvent.UpdateCompleted(payload.EffectId, payload.IdempotencyKey, completedSurface, candidate),
            SalesforceConstants.OutcomeEventId(payload.EffectId, SalesforceFeedEvent.UpdateCompletedKind));
        Outbox.Add(outcome);
        await WriteStateAsync(CancellationToken.None);

        using var activity = ActivitySource.StartActivity("salesforce.update.completed");
        activity?.SetTag("salesforce.providerRecordIdLength", result.ProviderRecordId.Length);
    }

    private async Task JournalTerminalFailureAsync(OutboxIntent<SalesforceFeedEvent> intent, SalesforceFeedEvent payload)
    {
        using var activity = ActivitySource.StartActivity("salesforce.update.failed");
        activity?.SetTag("salesforce.failure", "sanitized");

        var failedKey = SalesforceConstants.EffectFailedFlagPrefix + payload.IdempotencyKey;
        Flags[failedKey] = "1";
        const string failedSurface = "update-failed";
        Flags[SalesforceConstants.SurfaceTextFlag] = failedSurface;
        var nextUi = UiRevision + 1;
        Flags[ReactiveNeuronPipeline<SalesforceFeedEvent>.UiRevisionKey] = nextUi.ToString();
        Flags[ReactiveNeuronPipeline<SalesforceFeedEvent>.RevisionKey] = (CurrentRevision + 1).ToString();
        Failures.Add(new SanitizedFailure(
            Guid.NewGuid(),
            BrainErrors.FailureSanitized,
            ReactiveNeuronPipeline<SalesforceFeedEvent>.UnknownFailureMessage,
            DateTimeOffset.UtcNow,
            intent.Event.Metadata.CommandId,
            intent.Event.Metadata.EventId));

        var candidate = UiFeedCandidate.CreateFailure(BrainErrors.FailureSanitized);
        var failedOutcome = CreateFeedIntent(
            intent.Event.Metadata,
            SalesforceFeedEvent.UpdateFailed(payload.EffectId, payload.IdempotencyKey, failedSurface, candidate),
            SalesforceConstants.OutcomeEventId(payload.EffectId, SalesforceFeedEvent.UpdateFailedKind));
        Outbox.Add(failedOutcome);
        await WriteStateAsync(CancellationToken.None);
    }

    private async Task PublishOutcomeWithSeamAsync(OutboxIntent<SalesforceFeedEvent> intent)
    {
        var candidate = intent.Event.Payload.UiCandidate
            ?? throw new BrainException(
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<SalesforceFeedEvent>.UnknownFailureMessage);

        if (Flags.TryGetValue(SalesforceConstants.FailNextOutcomePublishFlag, out var raw)
            && int.TryParse(raw, out var remaining)
            && remaining > 0)
        {
            Flags[SalesforceConstants.FailNextOutcomePublishFlag] = (remaining - 1).ToString();
            await WriteStateAsync(CancellationToken.None);
            throw new InvalidOperationException("outcome publish failed");
        }

        await PublishUiFeedCandidateAsync(intent.Event.Metadata, candidate);

        var vertical = intent.Event with
        {
            Payload = intent.Event.Payload with { UiCandidate = null }
        };
        await PublishEventAsync(vertical, DefaultStreamProviderName, intent.StreamNamespace, intent.StreamId);
    }

    private async Task PublishUiFeedCandidateAsync(SynapseMetadata metadata, UiFeedCandidate candidate)
    {
        var synapse = new EventSynapse<UiFeedCandidate>(metadata, candidate);
        var stream = this.GetStreamProvider(DefaultStreamProviderName)
            .GetStream<EventSynapse<UiFeedCandidate>>(StreamId.Create(
                UiFeedStreams.CandidateNamespace,
                UiFeedStreams.StreamId(metadata.OrganizationId, metadata.SpaceId)));
        await stream.OnNextAsync(synapse);
    }

    private UiFeedCandidate CreateSnapshotCandidate(string surfaceText, long uiRevision) =>
        UiFeedCandidate.CreateSnapshot(new UiSurfaceSnapshot(new UiSurface(
            NeuronSurfaceId,
            uiRevision,
            [new UiBlock("text", surfaceText, [])])));

    private bool IsTerminal(string idempotencyKey) =>
        Flags.ContainsKey(SalesforceConstants.EffectDoneFlagPrefix + idempotencyKey)
        || Flags.ContainsKey(SalesforceConstants.EffectFailedFlagPrefix + idempotencyKey);

    private string ResolveSurfaceText()
    {
        if (Flags.TryGetValue(SalesforceConstants.SurfaceTextFlag, out var flagged) && !string.IsNullOrEmpty(flagged))
            return flagged;
        return string.IsNullOrEmpty(DomainState) ? "empty" : DomainState;
    }

    private OutboxIntent<SalesforceFeedEvent> CreateFeedIntent(
        SynapseMetadata metadata,
        SalesforceFeedEvent payload,
        Guid eventId)
    {
        var source = NeuronAddress.Parse(this.GetPrimaryKeyString());
        var @event = new EventSynapse<SalesforceFeedEvent>(
            metadata with
            {
                EventId = eventId,
                CausalDepth = metadata.CausalDepth + 1,
                Source = source,
            },
            payload);
        return OutboxIntent<SalesforceFeedEvent>.Create(
            SalesforceConstants.FeedStreamNamespace,
            NeuronFeedStreamId,
            @event);
    }
}
