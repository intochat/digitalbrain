using System.Diagnostics;
using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Streams;

namespace DigitalBrain.Google;

public sealed class GmailNeuron(
    [FromKeyedServices("gmail-receipts")] IDurableDictionary<string, CommandReceipt> receipts,
    [FromKeyedServices("gmail-events")] IDurableDictionary<string, byte> processedEvents,
    [FromKeyedServices("gmail-sequences")] IDurableDictionary<string, long> sourceSequences,
    [FromKeyedServices("gmail-outbox")] IDurableList<OutboxIntent<GmailFeedEvent>> outbox,
    [FromKeyedServices("gmail-domain")] IDurableDictionary<string, string> domain,
    [FromKeyedServices("gmail-flags")] IDurableDictionary<string, string> flags,
    [FromKeyedServices("gmail-failures")] IDurableList<SanitizedFailure> failures,
    [FromKeyedServices("gmail-accepted-causation")] IDurableDictionary<string, byte> acceptedCausation,
    [FromKeyedServices("gmail-rejected-causation")] IDurableDictionary<string, byte> rejectedCausation,
    IGmailMcpClient mcpClient,
    IChatClient chatClient) : ReactiveNeuron<GmailFeedEvent>(
        receipts,
        processedEvents,
        sourceSequences,
        outbox,
        domain,
        flags,
        failures,
        acceptedCausation,
        rejectedCausation), IGmail, IGmailNeuronControl
{
    public static readonly ActivitySource ActivitySource = new("DigitalBrain.Google.Gmail");

    private readonly IGmailMcpClient _mcpClient = mcpClient;
    private readonly IChatClient _chatClient = chatClient;
    private Guid _activationToken;

    private string NeuronSurfaceId => this.GetPrimaryKeyString();
    private Guid NeuronFeedStreamId => GmailConstants.FeedStreamIdFor(NeuronSurfaceId);

    protected override bool AutoDrainAfterCommit =>
        !Flags.TryGetValue(GmailConstants.AutoDrainFlag, out var value) || value != "0";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationToken = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());

    public Task<CommandReceipt> ListMessagesAsync(CommandSynapse<GmailListRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            using var activity = ActivitySource.StartActivity("gmail.list");
            activity?.SetTag("gmail.maxResults", payload.MaxResults);
            activity?.SetTag("gmail.queryLength", payload.Query.Length);

            var result = await _mcpClient.ListMessagesAsync(payload.Query, payload.MaxResults);
            var surfaceText = $"messages:{result.MessageCount}";
            Flags[GmailConstants.SurfaceTextFlag] = surfaceText;
            var nextUi = UiRevision + 1;
            var candidate = CreateSnapshotCandidate(surfaceText, nextUi);
            var intent = CreateFeedIntent(
                command.Metadata,
                GmailFeedEvent.UiSurface(surfaceText, candidate),
                GmailConstants.OutcomeEventId(command.Metadata.CommandId, GmailFeedEvent.UiSurfaceKind));
            await commit(new ReactiveCommit<GmailFeedEvent>(surfaceText, nextUi, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> SendMessageAsync(CommandSynapse<GmailSendRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            using var activity = ActivitySource.StartActivity("gmail.send");
            activity?.SetTag("gmail.subjectLength", payload.Subject.Length);
            activity?.SetTag("gmail.toLength", payload.To.Length);

            var idempotencyKey = command.Metadata.CommandId.ToString("N");
            if (IsTerminal(idempotencyKey))
            {
                var existing = ResolveSurfaceText();
                await commit(new ReactiveCommit<GmailFeedEvent>(existing, UiRevision, []));
                return CommandReceiptStatus.Accepted;
            }

            var effectId = command.Metadata.CommandId;
            var surfaceText = "send-pending";
            Flags[GmailConstants.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(
                command.Metadata,
                GmailFeedEvent.SendEffect(effectId, idempotencyKey, payload.To, payload.Subject, payload.Body),
                effectId);
            await commit(new ReactiveCommit<GmailFeedEvent>(surfaceText, UiRevision + 1, [intent]));
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

    public ChatClientAgent CreateAgent(Func<SynapseMetadata> metadataFactory) =>
        GmailMcpTools.CreateAgent(_chatClient, _mcpClient, this, metadataFactory);

    public IReadOnlyList<AITool> CreateAgentTools(Func<SynapseMetadata> metadataFactory) =>
        GmailMcpTools.CreateTypedTools(_mcpClient, this, metadataFactory);

    public async Task SetAutoDrainAsync(bool enabled)
    {
        Flags[GmailConstants.AutoDrainFlag] = enabled ? "1" : "0";
        await WriteStateAsync(CancellationToken.None);
    }

    public Task DrainOutboxAsync() => DrainOutboxCoreAsync(throwOnPublishFailure: false);

    public Task DrainOutboxStrictAsync() => DrainOutboxCoreAsync(throwOnPublishFailure: true);

    public Task<int> GetOutboxCountAsync() => Task.FromResult(Outbox.Count);

    public Task<OutboxIntent<GmailFeedEvent>?> PeekOutboxAsync() =>
        Task.FromResult(Outbox.Count == 0 ? null : Outbox[0]);

    public Task ReplayOutboxIntentAsync(OutboxIntent<GmailFeedEvent> intent) =>
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
        Flags[GmailConstants.FailNextOutcomePublishFlag] = count.ToString();
        await WriteStateAsync(CancellationToken.None);
    }

    public Task<bool> HasEffectTerminalAsync(string idempotencyKey) =>
        Task.FromResult(IsTerminal(idempotencyKey));

    protected override async Task PublishOutboxIntentAsync(OutboxIntent<GmailFeedEvent> intent)
    {
        var payload = intent.Event.Payload;
        if (payload.Kind is GmailFeedEvent.UiSurfaceKind
            or GmailFeedEvent.SendCompletedKind
            or GmailFeedEvent.SendFailedKind)
        {
            await PublishOutcomeWithSeamAsync(intent);
            return;
        }

        if (payload.Kind != GmailFeedEvent.SendEffectKind)
        {
            await base.PublishOutboxIntentAsync(intent);
            return;
        }

        var doneKey = GmailConstants.EffectDoneFlagPrefix + payload.IdempotencyKey;
        var failedKey = GmailConstants.EffectFailedFlagPrefix + payload.IdempotencyKey;
        if (Flags.ContainsKey(doneKey) || Flags.ContainsKey(failedKey))
            return;

        GmailSendResult result;
        try
        {
            result = await _mcpClient.SendMessageAsync(
                payload.To,
                payload.Subject,
                payload.Body,
                payload.IdempotencyKey);
        }
        catch (Exception)
        {
            await JournalTerminalFailureAsync(intent, payload);
            return;
        }

        const string completedSurface = "send-completed";
        Flags[doneKey] = "1";
        Flags[GmailConstants.SurfaceTextFlag] = completedSurface;
        var nextUi = UiRevision + 1;
        Flags[ReactiveNeuronPipeline<GmailFeedEvent>.UiRevisionKey] = nextUi.ToString();
        Flags[ReactiveNeuronPipeline<GmailFeedEvent>.RevisionKey] = (CurrentRevision + 1).ToString();

        var candidate = CreateSnapshotCandidate(completedSurface, nextUi);
        var outcome = CreateFeedIntent(
            intent.Event.Metadata,
            GmailFeedEvent.SendCompleted(payload.EffectId, payload.IdempotencyKey, completedSurface, candidate),
            GmailConstants.OutcomeEventId(payload.EffectId, GmailFeedEvent.SendCompletedKind));
        Outbox.Add(outcome);
        await WriteStateAsync(CancellationToken.None);

        using var activity = ActivitySource.StartActivity("gmail.send.completed");
        activity?.SetTag("gmail.providerMessageIdLength", result.ProviderMessageId.Length);
    }

    private async Task JournalTerminalFailureAsync(OutboxIntent<GmailFeedEvent> intent, GmailFeedEvent payload)
    {
        using var activity = ActivitySource.StartActivity("gmail.send.failed");
        activity?.SetTag("gmail.failure", "sanitized");

        var failedKey = GmailConstants.EffectFailedFlagPrefix + payload.IdempotencyKey;
        Flags[failedKey] = "1";
        const string failedSurface = "send-failed";
        Flags[GmailConstants.SurfaceTextFlag] = failedSurface;
        var nextUi = UiRevision + 1;
        Flags[ReactiveNeuronPipeline<GmailFeedEvent>.UiRevisionKey] = nextUi.ToString();
        Flags[ReactiveNeuronPipeline<GmailFeedEvent>.RevisionKey] = (CurrentRevision + 1).ToString();
        Failures.Add(new SanitizedFailure(
            Guid.NewGuid(),
            BrainErrors.FailureSanitized,
            ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage,
            DateTimeOffset.UtcNow,
            intent.Event.Metadata.CommandId,
            intent.Event.Metadata.EventId));

        var candidate = UiFeedCandidate.CreateFailure(BrainErrors.FailureSanitized);
        var failedOutcome = CreateFeedIntent(
            intent.Event.Metadata,
            GmailFeedEvent.SendFailed(payload.EffectId, payload.IdempotencyKey, failedSurface, candidate),
            GmailConstants.OutcomeEventId(payload.EffectId, GmailFeedEvent.SendFailedKind));
        Outbox.Add(failedOutcome);
        await WriteStateAsync(CancellationToken.None);
    }

    private async Task PublishOutcomeWithSeamAsync(OutboxIntent<GmailFeedEvent> intent)
    {
        if (Flags.TryGetValue(GmailConstants.FailNextOutcomePublishFlag, out var raw)
            && int.TryParse(raw, out var remaining)
            && remaining > 0)
        {
            Flags[GmailConstants.FailNextOutcomePublishFlag] = (remaining - 1).ToString();
            await WriteStateAsync(CancellationToken.None);
            throw new InvalidOperationException("outcome publish failed");
        }

        if (intent.Event.Payload.UiCandidate is { } candidate)
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
        Flags.ContainsKey(GmailConstants.EffectDoneFlagPrefix + idempotencyKey)
        || Flags.ContainsKey(GmailConstants.EffectFailedFlagPrefix + idempotencyKey);

    private string ResolveSurfaceText()
    {
        if (Flags.TryGetValue(GmailConstants.SurfaceTextFlag, out var flagged) && !string.IsNullOrEmpty(flagged))
            return flagged;
        return string.IsNullOrEmpty(DomainState) ? "empty" : DomainState;
    }

    private OutboxIntent<GmailFeedEvent> CreateFeedIntent(
        SynapseMetadata metadata,
        GmailFeedEvent payload,
        Guid eventId)
    {
        var source = NeuronAddress.Parse(this.GetPrimaryKeyString());
        var @event = new EventSynapse<GmailFeedEvent>(
            metadata with
            {
                EventId = eventId,
                CausalDepth = metadata.CausalDepth + 1,
                Source = source,
            },
            payload);
        return OutboxIntent<GmailFeedEvent>.Create(
            GmailConstants.FeedStreamNamespace,
            NeuronFeedStreamId,
            @event);
    }
}
