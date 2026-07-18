using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

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
    private readonly ISalesforceMcpClient _mcpClient = mcpClient;
    private readonly List<string> _telemetry = [];
    private Guid _activationToken;
    private int _providerUpdateCalls;

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
            RecordTelemetry($"salesforce.query soqlLength={payload.Soql.Length}");
            var result = await _mcpClient.QueryRecordsAsync(payload.Soql);
            var surfaceText = $"records:{result.RecordCount}";
            Flags[SalesforceConstants.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(command.Metadata, SalesforceFeedEvent.UiSurface(surfaceText));
            await commit(new ReactiveCommit<SalesforceFeedEvent>(surfaceText, UiRevision + 1, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> UpdateRecordAsync(CommandSynapse<SalesforceUpdateRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"salesforce.update objectTypeLength={payload.ObjectType.Length} fieldCount={payload.Fields.Count}");
            var idempotencyKey = command.Metadata.CommandId.ToString("N");
            var doneKey = SalesforceConstants.EffectDoneFlagPrefix + idempotencyKey;
            if (Flags.ContainsKey(doneKey))
            {
                var existing = ResolveSurfaceText();
                await commit(new ReactiveCommit<SalesforceFeedEvent>(existing, UiRevision, []));
                return CommandReceiptStatus.Accepted;
            }

            var effectId = Guid.CreateVersion7();
            var surfaceText = "update-pending";
            Flags[SalesforceConstants.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(
                command.Metadata,
                SalesforceFeedEvent.UpdateEffect(
                    effectId,
                    idempotencyKey,
                    payload.ObjectType,
                    payload.RecordId,
                    payload.Fields));
            await commit(new ReactiveCommit<SalesforceFeedEvent>(surfaceText, UiRevision + 1, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<UiSurfaceSnapshot> GetSurfaceAsync()
    {
        var text = ResolveSurfaceText();
        return Task.FromResult(new UiSurfaceSnapshot(new UiSurface(
            SalesforceConstants.SurfaceId,
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

    public Task<int> GetProviderUpdateCallsAsync() => Task.FromResult(_providerUpdateCalls);

    protected override async Task PublishOutboxIntentAsync(OutboxIntent<SalesforceFeedEvent> intent)
    {
        var payload = intent.Event.Payload;
        if (payload.Kind is SalesforceFeedEvent.UiSurfaceKind
            or SalesforceFeedEvent.UpdateCompletedKind
            or SalesforceFeedEvent.UpdateFailedKind)
        {
            await base.PublishOutboxIntentAsync(intent);
            return;
        }

        if (payload.Kind != SalesforceFeedEvent.UpdateEffectKind)
        {
            await base.PublishOutboxIntentAsync(intent);
            return;
        }

        var doneKey = SalesforceConstants.EffectDoneFlagPrefix + payload.IdempotencyKey;
        var failedKey = SalesforceConstants.EffectDoneFlagPrefix + "failed:" + payload.IdempotencyKey;
        if (Flags.ContainsKey(doneKey))
            return;
        if (Flags.ContainsKey(failedKey))
        {
            throw new BrainException(
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<SalesforceFeedEvent>.UnknownFailureMessage);
        }

        try
        {
            _providerUpdateCalls++;
            var result = await _mcpClient.UpdateRecordAsync(
                payload.ObjectType,
                payload.RecordId,
                payload.Fields,
                payload.IdempotencyKey);
            Flags[doneKey] = "1";
            const string completedSurface = "update-completed";
            Flags[SalesforceConstants.SurfaceTextFlag] = completedSurface;
            var nextUi = UiRevision + 1;
            Flags[ReactiveNeuronPipeline<SalesforceFeedEvent>.UiRevisionKey] = nextUi.ToString();
            Flags[ReactiveNeuronPipeline<SalesforceFeedEvent>.RevisionKey] = (CurrentRevision + 1).ToString();
            Outbox.Add(CreateFeedIntent(
                intent.Event.Metadata,
                SalesforceFeedEvent.UpdateCompleted(payload.EffectId, payload.IdempotencyKey, completedSurface)));
            RecordTelemetry($"salesforce.update.completed providerRecordIdLength={result.ProviderRecordId.Length}");
        }
        catch (Exception)
        {
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
            Outbox.Add(CreateFeedIntent(
                intent.Event.Metadata,
                SalesforceFeedEvent.UpdateFailed(payload.EffectId, payload.IdempotencyKey, failedSurface)));
            RecordTelemetry("salesforce.update.failed");
            throw new BrainException(
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<SalesforceFeedEvent>.UnknownFailureMessage);
        }
    }

    private string ResolveSurfaceText()
    {
        if (Flags.TryGetValue(SalesforceConstants.SurfaceTextFlag, out var flagged) && !string.IsNullOrEmpty(flagged))
            return flagged;
        return string.IsNullOrEmpty(DomainState) ? "empty" : DomainState;
    }

    private OutboxIntent<SalesforceFeedEvent> CreateFeedIntent(SynapseMetadata metadata, SalesforceFeedEvent payload)
    {
        var source = NeuronAddress.Parse(this.GetPrimaryKeyString());
        var @event = new EventSynapse<SalesforceFeedEvent>(
            metadata with
            {
                EventId = Guid.CreateVersion7(),
                CausalDepth = metadata.CausalDepth + 1,
                Source = source,
            },
            payload);
        return OutboxIntent<SalesforceFeedEvent>.Create(
            SalesforceConstants.FeedStreamNamespace,
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
