using Brain.Contracts;
using Brain.Kernel;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceReactiveCore
{
    public const string SurfaceId = "salesforce.surface";
    public const string FeedStreamNamespace = "salesforce.feed";
    public const string EffectDoneFlagPrefix = "effect-done:";
    public const string SurfaceTextFlag = "surface-text";

    private readonly IReactiveStore<SalesforceFeedEvent> _store;
    private readonly ReactiveNeuronPipeline<SalesforceFeedEvent> _pipeline;
    private readonly ISalesforceMcpClient _mcp;
    private readonly List<string> _telemetry;
    private readonly List<string> _lifecycleOrder;
    private readonly Guid _feedStreamId;
    private readonly NeuronAddress _self;

    public SalesforceReactiveCore(
        IReactiveStore<SalesforceFeedEvent> store,
        ISalesforceMcpClient mcp,
        NeuronAddress self,
        Guid? feedStreamId = null)
    {
        _store = store;
        _mcp = mcp;
        _pipeline = new ReactiveNeuronPipeline<SalesforceFeedEvent>(store);
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
    public IList<OutboxIntent<SalesforceFeedEvent>> Outbox => _store.Outbox;
    public IList<SanitizedFailure> Failures => _store.Failures;
    public IDictionary<string, string> Flags => _store.Flags;

    public Task<CommandReceipt> QueryRecordsAsync(CommandSynapse<SalesforceQueryRequest> command) =>
        _pipeline.ExecuteCommandAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"salesforce.query soqlLength={payload.Soql.Length}");
            var result = await _mcp.QueryRecordsAsync(payload.Soql);
            var surfaceText = $"records:{result.RecordCount}";
            _store.Flags[SurfaceTextFlag] = surfaceText;
            var feed = CreateOutbox(command.Metadata, SalesforceFeedEvent.UiSurface(surfaceText));
            await DurableCommitAsync(commit, surfaceText, UiRevision + 1, [feed]);
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> UpdateRecordAsync(CommandSynapse<SalesforceUpdateRequest> command) =>
        _pipeline.ExecuteCommandAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"salesforce.update objectTypeLength={payload.ObjectType.Length} fieldCount={payload.Fields.Count}");
            var idempotencyKey = command.Metadata.CommandId.ToString("N");
            var doneKey = EffectDoneFlagPrefix + idempotencyKey;
            if (_store.Flags.ContainsKey(doneKey))
            {
                var existingSurface = _store.Flags.TryGetValue(SurfaceTextFlag, out var text) ? text : DomainState;
                await DurableCommitAsync(commit, existingSurface, UiRevision, []);
                return CommandReceiptStatus.Accepted;
            }

            var effectId = Guid.NewGuid();
            var surfaceText = "update-pending";
            _store.Flags[SurfaceTextFlag] = surfaceText;
            var effect = CreateOutbox(
                command.Metadata,
                SalesforceFeedEvent.UpdateEffect(
                    effectId,
                    idempotencyKey,
                    payload.ObjectType,
                    payload.RecordId,
                    payload.Fields));
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

    public async Task ExecuteOutboxIntentAsync(OutboxIntent<SalesforceFeedEvent> intent)
    {
        var payload = intent.Event.Payload;
        if (payload.Kind == SalesforceFeedEvent.UpdateEffectKind)
        {
            var doneKey = EffectDoneFlagPrefix + payload.IdempotencyKey;
            if (_store.Flags.ContainsKey(doneKey))
                return;

            try
            {
                var result = await _mcp.UpdateRecordAsync(
                    payload.ObjectType,
                    payload.RecordId,
                    payload.Fields,
                    payload.IdempotencyKey);
                _store.Flags[doneKey] = "1";
                _store.Flags[SurfaceTextFlag] = "update-completed";
                _store.Domain[ReactiveNeuronPipeline<SalesforceFeedEvent>.DomainStateKey] = "update-completed";
                RecordTelemetry($"salesforce.update.completed providerRecordIdLength={result.ProviderRecordId.Length}");
            }
            catch (Exception)
            {
                _store.Failures.Add(new SanitizedFailure(
                    Guid.NewGuid(),
                    BrainErrors.FailureSanitized,
                    ReactiveNeuronPipeline<SalesforceFeedEvent>.UnknownFailureMessage,
                    DateTimeOffset.UtcNow,
                    intent.Event.Metadata.CommandId,
                    intent.Event.Metadata.EventId));
                RecordTelemetry("salesforce.update.failed");
                throw new BrainException(
                    BrainErrors.FailureSanitized,
                    ReactiveNeuronPipeline<SalesforceFeedEvent>.UnknownFailureMessage);
            }

            return;
        }

        RecordTelemetry($"salesforce.feed kind={payload.Kind}");
    }

    private async Task DurableCommitAsync(
        CommitReactionAsync<SalesforceFeedEvent> commit,
        string domainState,
        long uiRevision,
        IReadOnlyList<OutboxIntent<SalesforceFeedEvent>> outbox)
    {
        _lifecycleOrder.Add("commit");
        await commit(new ReactiveCommit<SalesforceFeedEvent>(domainState, uiRevision, outbox));
    }

    private OutboxIntent<SalesforceFeedEvent> CreateOutbox(SynapseMetadata metadata, SalesforceFeedEvent payload)
    {
        var @event = new EventSynapse<SalesforceFeedEvent>(
            metadata with
            {
                EventId = Guid.NewGuid(),
                CausalDepth = metadata.CausalDepth + 1,
                Source = _self,
            },
            payload);
        return OutboxIntent<SalesforceFeedEvent>.Create(FeedStreamNamespace, _feedStreamId, @event);
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
