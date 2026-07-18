using System.Text.Json;
using Brain.Contracts;
using Brain.Kernel;

namespace DigitalBrain.Google;

public sealed class GmailReactiveCore
{
    public const string SurfaceId = "gmail.surface";
    public const string FeedStreamNamespace = "gmail.feed";
    public const string EffectDoneFlagPrefix = "effect-done:";
    public const string SurfaceTextFlag = "surface-text";

    private readonly IReactiveStore<GmailFeedEvent> _store;
    private readonly ReactiveNeuronPipeline<GmailFeedEvent> _pipeline;
    private readonly IGmailMcpClient _mcp;
    private readonly List<string> _telemetry;
    private readonly List<string> _lifecycleOrder;
    private readonly Guid _feedStreamId;
    private readonly NeuronAddress _self;

    public GmailReactiveCore(
        IReactiveStore<GmailFeedEvent> store,
        IGmailMcpClient mcp,
        NeuronAddress self,
        Guid? feedStreamId = null)
    {
        _store = store;
        _mcp = mcp;
        _pipeline = new ReactiveNeuronPipeline<GmailFeedEvent>(store);
        _telemetry = [];
        _lifecycleOrder = [];
        _feedStreamId = feedStreamId ?? Guid.NewGuid();
        _self = self;
    }

    public IReadOnlyList<string> Telemetry => _telemetry;
    public IReadOnlyList<string> LifecycleOrder => _lifecycleOrder;
    public long UiRevision => _pipeline.UiRevision;
    public long Revision => _pipeline.CurrentRevision;
    public string DomainState => _pipeline.DomainState;
    public IList<OutboxIntent<GmailFeedEvent>> Outbox => _store.Outbox;
    public IList<SanitizedFailure> Failures => _store.Failures;
    public IDictionary<string, string> Flags => _store.Flags;

    public Task<CommandReceipt> ListMessagesAsync(CommandSynapse<GmailListRequest> command) =>
        _pipeline.ExecuteCommandAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"gmail.list maxResults={payload.MaxResults} queryLength={payload.Query.Length}");
            var result = await _mcp.ListMessagesAsync(payload.Query, payload.MaxResults);
            var surfaceText = $"messages:{result.MessageCount}";
            _store.Flags[SurfaceTextFlag] = surfaceText;
            var feed = CreateOutbox(
                command.Metadata,
                GmailFeedEvent.UiSurface(surfaceText));
            await DurableCommitAsync(commit, surfaceText, UiRevision + 1, [feed]);
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> SendMessageAsync(CommandSynapse<GmailSendRequest> command) =>
        _pipeline.ExecuteCommandAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"gmail.send subjectLength={payload.Subject.Length} toLength={payload.To.Length}");
            var idempotencyKey = command.Metadata.CommandId.ToString("N");
            var doneKey = EffectDoneFlagPrefix + idempotencyKey;
            if (_store.Flags.ContainsKey(doneKey))
            {
                var existingSurface = _store.Flags.TryGetValue(SurfaceTextFlag, out var text) ? text : DomainState;
                await DurableCommitAsync(commit, existingSurface, UiRevision, []);
                return CommandReceiptStatus.Accepted;
            }

            var effectId = Guid.NewGuid();
            var surfaceText = "send-pending";
            _store.Flags[SurfaceTextFlag] = surfaceText;
            var effect = CreateOutbox(
                command.Metadata,
                GmailFeedEvent.SendEffect(effectId, idempotencyKey, payload.To, payload.Subject, payload.Body));
            await DurableCommitAsync(commit, surfaceText, UiRevision + 1, [effect]);
            return CommandReceiptStatus.Accepted;
        });

    public async Task DrainOutboxAsync()
    {
        while (_store.Outbox.Count > 0)
        {
            var intent = _store.Outbox[0];
            await ExecuteOutboxIntentAsync(intent);
            _store.Outbox.RemoveAt(0);
            await _store.CommitAsync();
        }
    }

    public UiSurfaceSnapshot GetSurface()
    {
        var text = _store.Flags.TryGetValue(SurfaceTextFlag, out var flagged) && !string.IsNullOrEmpty(flagged)
            ? flagged
            : (string.IsNullOrEmpty(DomainState) ? "empty" : DomainState);
        return new UiSurfaceSnapshot(new UiSurface(
            SurfaceId,
            UiRevision,
            [new UiBlock("text", text, [])]));
    }

    public async Task ExecuteOutboxIntentAsync(OutboxIntent<GmailFeedEvent> intent)
    {
        var payload = intent.Event.Payload;
        if (payload.Kind == GmailFeedEvent.SendEffectKind)
        {
            var doneKey = EffectDoneFlagPrefix + payload.IdempotencyKey;
            if (_store.Flags.ContainsKey(doneKey))
                return;

            try
            {
                var result = await _mcp.SendMessageAsync(
                    payload.To,
                    payload.Subject,
                    payload.Body,
                    payload.IdempotencyKey);
                _store.Flags[doneKey] = "1";
                _store.Flags[SurfaceTextFlag] = "send-completed";
                _store.Domain[ReactiveNeuronPipeline<GmailFeedEvent>.DomainStateKey] = "send-completed";
                RecordTelemetry($"gmail.send.completed providerMessageIdLength={result.ProviderMessageId.Length}");
            }
            catch (Exception)
            {
                _store.Failures.Add(new SanitizedFailure(
                    Guid.NewGuid(),
                    BrainErrors.FailureSanitized,
                    ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage,
                    DateTimeOffset.UtcNow,
                    intent.Event.Metadata.CommandId,
                    intent.Event.Metadata.EventId));
                RecordTelemetry("gmail.send.failed");
                throw new BrainException(BrainErrors.FailureSanitized, ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage);
            }

            return;
        }

        RecordTelemetry($"gmail.feed kind={payload.Kind}");
    }

    private async Task DurableCommitAsync(
        CommitReactionAsync<GmailFeedEvent> commit,
        string domainState,
        long uiRevision,
        IReadOnlyList<OutboxIntent<GmailFeedEvent>> outbox)
    {
        _lifecycleOrder.Add("commit");
        await commit(new ReactiveCommit<GmailFeedEvent>(domainState, uiRevision, outbox));
    }

    private OutboxIntent<GmailFeedEvent> CreateOutbox(SynapseMetadata metadata, GmailFeedEvent payload)
    {
        var @event = new EventSynapse<GmailFeedEvent>(
            metadata with
            {
                EventId = Guid.NewGuid(),
                CausalDepth = metadata.CausalDepth + 1,
                Source = _self,
            },
            payload);
        return OutboxIntent<GmailFeedEvent>.Create(FeedStreamNamespace, _feedStreamId, @event);
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

    public static string SerializeDomain(string surfaceText) =>
        JsonSerializer.Serialize(new { surface = surfaceText });
}
