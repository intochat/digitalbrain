using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

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
    private readonly IGmailMcpClient _mcpClient = mcpClient;
    private readonly IChatClient _chatClient = chatClient;
    private readonly List<string> _telemetry = [];
    private Guid _activationToken;
    private int _providerSendCalls;

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
            RecordTelemetry($"gmail.list maxResults={payload.MaxResults} queryLength={payload.Query.Length}");
            var result = await _mcpClient.ListMessagesAsync(payload.Query, payload.MaxResults);
            var surfaceText = $"messages:{result.MessageCount}";
            Flags[GmailConstants.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(command.Metadata, GmailFeedEvent.UiSurface(surfaceText));
            await commit(new ReactiveCommit<GmailFeedEvent>(surfaceText, UiRevision + 1, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> SendMessageAsync(CommandSynapse<GmailSendRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"gmail.send subjectLength={payload.Subject.Length} toLength={payload.To.Length}");
            var idempotencyKey = command.Metadata.CommandId.ToString("N");
            var doneKey = GmailConstants.EffectDoneFlagPrefix + idempotencyKey;
            if (Flags.ContainsKey(doneKey))
            {
                var existing = ResolveSurfaceText();
                await commit(new ReactiveCommit<GmailFeedEvent>(existing, UiRevision, []));
                return CommandReceiptStatus.Accepted;
            }

            var effectId = Guid.CreateVersion7();
            var surfaceText = "send-pending";
            Flags[GmailConstants.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(
                command.Metadata,
                GmailFeedEvent.SendEffect(effectId, idempotencyKey, payload.To, payload.Subject, payload.Body));
            await commit(new ReactiveCommit<GmailFeedEvent>(surfaceText, UiRevision + 1, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<UiSurfaceSnapshot> GetSurfaceAsync()
    {
        var text = ResolveSurfaceText();
        return Task.FromResult(new UiSurfaceSnapshot(new UiSurface(
            GmailConstants.SurfaceId,
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

    public Task<IReadOnlyList<string>> GetTelemetryAsync() =>
        Task.FromResult<IReadOnlyList<string>>(_telemetry.ToArray());

    public Task<SanitizedFailure?> GetLastFailureAsync() =>
        Task.FromResult(Failures.Count == 0 ? null : Failures[^1]);

    public Task<Guid> GetActivationTokenAsync() => Task.FromResult(_activationToken);

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task<int> GetProviderSendCallsAsync() => Task.FromResult(_providerSendCalls);

    protected override async Task PublishOutboxIntentAsync(OutboxIntent<GmailFeedEvent> intent)
    {
        var payload = intent.Event.Payload;
        if (payload.Kind is GmailFeedEvent.UiSurfaceKind
            or GmailFeedEvent.SendCompletedKind
            or GmailFeedEvent.SendFailedKind)
        {
            await base.PublishOutboxIntentAsync(intent);
            return;
        }

        if (payload.Kind != GmailFeedEvent.SendEffectKind)
        {
            await base.PublishOutboxIntentAsync(intent);
            return;
        }

        var doneKey = GmailConstants.EffectDoneFlagPrefix + payload.IdempotencyKey;
        var failedKey = GmailConstants.EffectDoneFlagPrefix + "failed:" + payload.IdempotencyKey;
        if (Flags.ContainsKey(doneKey))
            return;
        if (Flags.ContainsKey(failedKey))
        {
            throw new BrainException(
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage);
        }

        try
        {
            _providerSendCalls++;
            var result = await _mcpClient.SendMessageAsync(
                payload.To,
                payload.Subject,
                payload.Body,
                payload.IdempotencyKey);
            Flags[doneKey] = "1";
            const string completedSurface = "send-completed";
            Flags[GmailConstants.SurfaceTextFlag] = completedSurface;
            var nextUi = UiRevision + 1;
            Flags[ReactiveNeuronPipeline<GmailFeedEvent>.UiRevisionKey] = nextUi.ToString();
            Flags[ReactiveNeuronPipeline<GmailFeedEvent>.RevisionKey] = (CurrentRevision + 1).ToString();
            Outbox.Add(CreateFeedIntent(
                intent.Event.Metadata,
                GmailFeedEvent.SendCompleted(payload.EffectId, payload.IdempotencyKey, completedSurface)));
            RecordTelemetry($"gmail.send.completed providerMessageIdLength={result.ProviderMessageId.Length}");
        }
        catch (Exception)
        {
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
            Outbox.Add(CreateFeedIntent(
                intent.Event.Metadata,
                GmailFeedEvent.SendFailed(payload.EffectId, payload.IdempotencyKey, failedSurface)));
            RecordTelemetry("gmail.send.failed");
            throw new BrainException(
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage);
        }
    }

    private string ResolveSurfaceText()
    {
        if (Flags.TryGetValue(GmailConstants.SurfaceTextFlag, out var flagged) && !string.IsNullOrEmpty(flagged))
            return flagged;
        return string.IsNullOrEmpty(DomainState) ? "empty" : DomainState;
    }

    private OutboxIntent<GmailFeedEvent> CreateFeedIntent(SynapseMetadata metadata, GmailFeedEvent payload)
    {
        var source = NeuronAddress.Parse(this.GetPrimaryKeyString());
        var @event = new EventSynapse<GmailFeedEvent>(
            metadata with
            {
                EventId = Guid.CreateVersion7(),
                CausalDepth = metadata.CausalDepth + 1,
                Source = source,
            },
            payload);
        return OutboxIntent<GmailFeedEvent>.Create(
            GmailConstants.FeedStreamNamespace,
            metadata.CommandId,
            @event);
    }

    private void RecordTelemetry(string entry)
    {
        if (entry.Contains("password", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("token", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("credential", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("telemetry rejected sensitive keyword");
        }

        _telemetry.Add(entry);
    }
}
