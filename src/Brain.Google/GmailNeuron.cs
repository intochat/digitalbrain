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
        rejectedCausation), IGmail
{
    private readonly IGmailMcpClient _mcpClient = mcpClient;
    private readonly IChatClient _chatClient = chatClient;
    private readonly List<string> _telemetry = [];

    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());

    public Task<CommandReceipt> ListMessagesAsync(CommandSynapse<GmailListRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"gmail.list maxResults={payload.MaxResults} queryLength={payload.Query.Length}");
            var result = await _mcpClient.ListMessagesAsync(payload.Query, payload.MaxResults);
            var surfaceText = $"messages:{result.MessageCount}";
            Flags[GmailReactiveCore.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(command.Metadata, GmailFeedEvent.UiSurface(surfaceText));
            await commit(new ReactiveCommit<GmailFeedEvent>(surfaceText, UiRevision + 1, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> SendMessageAsync(CommandSynapse<GmailSendRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            RecordTelemetry($"gmail.send subjectLength={payload.Subject.Length} toLength={payload.To.Length}");
            var idempotencyKey = command.Metadata.CommandId.ToString("N");
            var doneKey = GmailReactiveCore.EffectDoneFlagPrefix + idempotencyKey;
            if (Flags.ContainsKey(doneKey))
            {
                var existing = Flags.TryGetValue(GmailReactiveCore.SurfaceTextFlag, out var text) ? text : DomainState;
                await commit(new ReactiveCommit<GmailFeedEvent>(existing, UiRevision, []));
                return CommandReceiptStatus.Accepted;
            }

            var effectId = Guid.NewGuid();
            var surfaceText = "send-pending";
            Flags[GmailReactiveCore.SurfaceTextFlag] = surfaceText;
            var intent = CreateFeedIntent(
                command.Metadata,
                GmailFeedEvent.SendEffect(effectId, idempotencyKey, payload.To, payload.Subject, payload.Body));
            await commit(new ReactiveCommit<GmailFeedEvent>(surfaceText, UiRevision + 1, [intent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<UiSurfaceSnapshot> GetSurfaceAsync()
    {
        var text = Flags.TryGetValue(GmailReactiveCore.SurfaceTextFlag, out var flagged) && !string.IsNullOrEmpty(flagged)
            ? flagged
            : (string.IsNullOrEmpty(DomainState) ? "empty" : DomainState);
        return Task.FromResult(new UiSurfaceSnapshot(new UiSurface(
            GmailReactiveCore.SurfaceId,
            UiRevision,
            [new UiBlock("text", text, [])])));
    }

    public ChatClientAgent CreateAgent() => GmailMcpTools.CreateAgent(_chatClient, _mcpClient);

    public IReadOnlyList<string> TelemetrySnapshot => _telemetry;

    protected override async Task PublishOutboxIntentAsync(OutboxIntent<GmailFeedEvent> intent)
    {
        var payload = intent.Event.Payload;
        if (payload.Kind != GmailFeedEvent.SendEffectKind)
        {
            await base.PublishOutboxIntentAsync(intent);
            return;
        }

        var doneKey = GmailReactiveCore.EffectDoneFlagPrefix + payload.IdempotencyKey;
        if (Flags.ContainsKey(doneKey))
            return;

        try
        {
            var result = await _mcpClient.SendMessageAsync(
                payload.To,
                payload.Subject,
                payload.Body,
                payload.IdempotencyKey);
            Flags[doneKey] = "1";
            Flags[GmailReactiveCore.SurfaceTextFlag] = "send-completed";
            RecordTelemetry($"gmail.send.completed providerMessageIdLength={result.ProviderMessageId.Length}");
        }
        catch (Exception)
        {
            Failures.Add(new SanitizedFailure(
                Guid.NewGuid(),
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage,
                DateTimeOffset.UtcNow,
                intent.Event.Metadata.CommandId,
                intent.Event.Metadata.EventId));
            RecordTelemetry("gmail.send.failed");
            throw new BrainException(
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<GmailFeedEvent>.UnknownFailureMessage);
        }
    }

    private OutboxIntent<GmailFeedEvent> CreateFeedIntent(SynapseMetadata metadata, GmailFeedEvent payload)
    {
        var source = NeuronAddress.Parse(this.GetPrimaryKeyString());
        var @event = new EventSynapse<GmailFeedEvent>(
            metadata with
            {
                EventId = Guid.NewGuid(),
                CausalDepth = metadata.CausalDepth + 1,
                Source = source,
            },
            payload);
        return OutboxIntent<GmailFeedEvent>.Create(
            GmailReactiveCore.FeedStreamNamespace,
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
