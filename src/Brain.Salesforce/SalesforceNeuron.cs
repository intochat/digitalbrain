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
        rejectedCausation), ISalesforce
{
    private readonly ISalesforceMcpClient _mcpClient = mcpClient;
    private readonly List<string> _telemetry = [];

    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());

    public Task<CommandReceipt> QueryRecordsAsync(CommandSynapse<SalesforceQueryRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"salesforce.query soqlLength={payload.Soql.Length}");
            var result = await _mcpClient.QueryRecordsAsync(payload.Soql);
            var surfaceText = $"records:{result.RecordCount}";
            Flags[SalesforceReactiveCore.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(command.Metadata, SalesforceFeedEvent.UiSurface(surfaceText));
            await commit(new ReactiveCommit<SalesforceFeedEvent>(surfaceText, UiRevision + 1, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> UpdateRecordAsync(CommandSynapse<SalesforceUpdateRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"salesforce.update objectTypeLength={payload.ObjectType.Length} fieldCount={payload.Fields.Count}");
            var idempotencyKey = command.Metadata.CommandId.ToString("N");
            var doneKey = SalesforceReactiveCore.EffectDoneFlagPrefix + idempotencyKey;
            if (Flags.ContainsKey(doneKey))
            {
                var existing = Flags.TryGetValue(SalesforceReactiveCore.SurfaceTextFlag, out var text) ? text : DomainState;
                await commit(new ReactiveCommit<SalesforceFeedEvent>(existing, UiRevision, []));
                return CommandReceiptStatus.Accepted;
            }

            var effectId = Guid.NewGuid();
            var surfaceText = "update-pending";
            Flags[SalesforceReactiveCore.SurfaceTextFlag] = surfaceText;
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
        var text = Flags.TryGetValue(SalesforceReactiveCore.SurfaceTextFlag, out var flagged) && !string.IsNullOrEmpty(flagged)
            ? flagged
            : (string.IsNullOrEmpty(DomainState) ? "empty" : DomainState);
        return Task.FromResult(new UiSurfaceSnapshot(new UiSurface(
            SalesforceReactiveCore.SurfaceId,
            UiRevision,
            [new UiBlock("text", text, [])])));
    }

    public IReadOnlyList<string> TelemetrySnapshot => _telemetry;

    protected override async Task PublishOutboxIntentAsync(OutboxIntent<SalesforceFeedEvent> intent)
    {
        var payload = intent.Event.Payload;
        if (payload.Kind != SalesforceFeedEvent.UpdateEffectKind)
        {
            await base.PublishOutboxIntentAsync(intent);
            return;
        }

        var doneKey = SalesforceReactiveCore.EffectDoneFlagPrefix + payload.IdempotencyKey;
        if (Flags.ContainsKey(doneKey))
            return;

        try
        {
            var result = await _mcpClient.UpdateRecordAsync(
                payload.ObjectType,
                payload.RecordId,
                payload.Fields,
                payload.IdempotencyKey);
            Flags[doneKey] = "1";
            Flags[SalesforceReactiveCore.SurfaceTextFlag] = "update-completed";
            RecordTelemetry($"salesforce.update.completed providerRecordIdLength={result.ProviderRecordId.Length}");
        }
        catch (Exception)
        {
            Failures.Add(new SanitizedFailure(
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
    }

    private OutboxIntent<SalesforceFeedEvent> CreateFeedIntent(SynapseMetadata metadata, SalesforceFeedEvent payload)
    {
        var source = NeuronAddress.Parse(this.GetPrimaryKeyString());
        var @event = new EventSynapse<SalesforceFeedEvent>(
            metadata with
            {
                EventId = Guid.NewGuid(),
                CausalDepth = metadata.CausalDepth + 1,
                Source = source,
            },
            payload);
        return OutboxIntent<SalesforceFeedEvent>.Create(
            SalesforceReactiveCore.FeedStreamNamespace,
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
