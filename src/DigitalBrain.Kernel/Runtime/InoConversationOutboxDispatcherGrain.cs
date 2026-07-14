using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Runtime;

[GrainType("digitalbrain.runtime.ino-conversation-outbox-dispatcher.v1")]
internal sealed class InoConversationOutboxDispatcherGrain(IGrainFactory grainFactory, TimeProvider timeProvider) : Grain, IInoConversationOutboxDispatcherGrain, IRemindable
{
    private const string ReminderName = "ino.conversation-outbox-dispatcher.execute.v1";
    private static readonly TimeSpan ReminderDueTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TimerInitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TimerRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Ino.Outbox");
    private IGrainReminder? _reminder;
    private IGrainTimer? _timer;

    public async Task ScheduleAsync()
    {
        _reminder ??= await this.RegisterOrUpdateReminder(ReminderName, ReminderDueTime, ReminderPeriod);
        EnsureTimer(TimerInitialDelay);
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, ReminderName, StringComparison.Ordinal)) return;
        await ProcessScheduledAsync();
    }

    private async Task ReceiveTimerAsync(CancellationToken cancellationToken)
    {
        var timer = _timer;
        _timer = null;
        timer?.Dispose();
        await ProcessScheduledAsync();
    }

    private async Task ProcessScheduledAsync()
    {

        var conversationGrainKey = this.GetPrimaryKeyString() ?? throw new InvalidOperationException("Outbox dispatchers require a conversation string key.");
        RuntimeStateKeys.DemandScopeHash(conversationGrainKey);
        await DispatchScheduledAsync(conversationGrainKey);

        var state = await grainFactory.GetGrain<IConversationNeuron>(conversationGrainKey).ReadAsync();
        if (state.Outbox.Any(entry => entry.DispatchedAt is null))
            EnsureTimer(TimerRetryDelay);
        else
            await StopReminderAsync();
    }

    private void EnsureTimer(TimeSpan dueTime) =>
        _timer ??= this.RegisterGrainTimer(ReceiveTimerAsync, new GrainTimerCreationOptions(dueTime, Timeout.InfiniteTimeSpan) { KeepAlive = true });

    private async Task DispatchScheduledAsync(string conversationGrainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationGrainKey);
        var conversation = grainFactory.GetGrain<IConversationNeuron>(conversationGrainKey);
        var state = await conversation.ReadAsync();
        if (state.Identity is null || state.Lifecycle == ConversationLifecycle.Tombstoned) return;

        foreach (var entry in state.Outbox.Where(entry => entry.DispatchedAt is null)
                     .OrderBy(entry => entry.Sequence == 0 ? 0 : 1)
                     .ThenBy(entry => entry.Sequence)
                     .ThenBy(entry => entry.CreatedAt)
                     .ThenBy(entry => entry.OutboxId, StringComparer.Ordinal)
                     .ToArray())
        {
            if (!string.Equals(entry.Kind, "surface-feed", StringComparison.Ordinal))
                throw new RuntimeStateIntegrityException("unknown conversation outbox kind");

            using var activity = ActivitySource.StartActivity("ino.outbox.dispatch", ActivityKind.Internal);
            activity?.SetTag("db.ino.outbox_id", entry.OutboxId);
            activity?.SetTag("db.ino.outbox_sequence", entry.Sequence);
            if (OperationOutboxRecord.TryRead(entry.PayloadUtf8, out var correlation) && correlation is not null)
            {
                activity?.SetTag("db.request.id", correlation.RequestId);
                activity?.SetTag("db.ino.operation_id", correlation.OperationId);
                activity?.SetTag("db.ino.conversation_grain", correlation.ConversationGrainKey);
                activity?.SetTag("db.ino.workflow_id", correlation.Workflow?.WorkflowId);
                activity?.SetTag("db.ino.workflow_session_id", correlation.Workflow?.SessionId);
                activity?.SetTag("db.ino.tool_id", correlation.ToolId);
                activity?.SetTag("db.ino.effect_id", correlation.EffectId);
            }
            var delivered = await ProjectAsync(state, entry).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            if (!delivered) break;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    state = await conversation.MarkOutboxDispatchedAsync(state.Revision, entry.OutboxId, timeProvider.GetUtcNow());
                    activity?.SetTag("db.ino.outcome", "dispatched");
                    break;
                }
                catch (RuntimeStateConflictException) when (attempt < 2)
                {
                    state = await conversation.ReadAsync();
                    if (state.Outbox.FirstOrDefault(candidate =>
                            string.Equals(candidate.OutboxId, entry.OutboxId, StringComparison.Ordinal))?.DispatchedAt is not null)
                        break;
                }
            }
        }
    }

    private async Task<bool> ProjectAsync(ConversationState conversation, ConversationOutboxEntry entry)
    {
        var identity = conversation.Identity ?? throw new RuntimeStateIntegrityException("conversation identity is missing");
        if (!OperationOutboxRecord.TryRead(entry.PayloadUtf8, out var record) || record is null)
            return false;
        if (!string.Equals(record.EventId, entry.OutboxId, StringComparison.Ordinal) ||
            !string.Equals(record.ConversationId, identity.ConversationId, StringComparison.Ordinal))
            throw new RuntimeStateIntegrityException("conversation outbox projection identity mismatch");
        var feed = grainFactory.GetGrain<ISurfaceFeedNeuron>(RuntimeStateKeys.SurfaceFeed(identity.OwnerId, identity.ActorId));
        var state = await EnsureFeedAsync(feed, identity);
        if (!TargetsConversation(state, identity.ConversationId)) return false;
        var bindingIssuedAt = timeProvider.GetUtcNow();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (state.AppliedProjectionIds.Contains(entry.OutboxId, StringComparer.Ordinal)) return true;
            var projection = CreateProjection(state, record, bindingIssuedAt);
            try
            {
                await feed.ApplyProjectionAsync(state.Revision, projection, timeProvider.GetUtcNow());
                return true;
            }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await feed.ReadAsync();
                if (!TargetsConversation(state, identity.ConversationId)) return false;
            }
        }
        throw new InvalidOperationException("Surface-feed projection revision retry exhausted.");
    }

    private static async Task<SurfaceFeedState> EnsureFeedAsync(ISurfaceFeedNeuron feed, ConversationIdentity identity)
    {
        var state = await feed.ReadAsync();
        var expected = new SurfaceFeedIdentity(identity.OwnerId, identity.ActorId);
        if (state.Identity is not null)
        {
            if (state.Identity != expected) throw new UnauthorizedAccessException("Surface-feed identity denied.");
            return state;
        }
        try
        {
            return await feed.InitializeAsync(state.Revision, expected);
        }
        catch (RuntimeStateConflictException)
        {
            state = await feed.ReadAsync();
            if (state.Identity != expected) throw new UnauthorizedAccessException("Surface-feed identity denied.");
            return state;
        }
    }

    private static bool TargetsConversation(SurfaceFeedState state, string conversationId)
    {
        var current = state.CurrentSurfaces.FirstOrDefault(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        if (current is null) return true;
        try
        {
            using var document = JsonDocument.Parse(current.PayloadUtf8);
            var root = document.RootElement;
            var presentation = root.Deserialize<SurfaceFeedPresentation>();
            return presentation is not null && string.Equals(presentation.CauseKind, "conversation", StringComparison.Ordinal) &&
                   string.Equals(presentation.CauseId, conversationId, StringComparison.Ordinal) &&
                   IsCanonicalConversationContent(presentation) &&
                   SurfaceFeedPresentationCompatibility.HasSupportedShape(root, presentation);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsCanonicalConversationContent(SurfaceFeedPresentation presentation) =>
        !string.IsNullOrWhiteSpace(presentation.CorrelationId) && presentation.RequiredClientCapabilities is not null &&
        presentation.RequiredClientCapabilities.SequenceEqual(ConversationSurfacePayload.RequiredCapabilities, StringComparer.Ordinal) &&
        presentation.Payload.ValueKind == JsonValueKind.Object &&
        presentation.Payload.TryGetProperty("kind", out var kind) &&
        kind.ValueKind == JsonValueKind.String &&
        string.Equals(kind.GetString(), "native", StringComparison.Ordinal) &&
        presentation.Payload.TryGetProperty("nativeKind", out var nativeKind) &&
        nativeKind.ValueKind == JsonValueKind.String &&
        string.Equals(nativeKind.GetString(), "inoConversation", StringComparison.Ordinal) &&
        presentation.Payload.TryGetProperty("data", out var data) &&
        data.ValueKind == JsonValueKind.Object;

    private static SurfaceFeedProjection CreateProjection(SurfaceFeedState state, OperationOutboxRecord record, DateTimeOffset bindingIssuedAt)
    {
        var conversation = record.ToSnapshot();
        var payload = ConversationSurfacePayload.Build(conversation);
        var descriptors = ConversationSurfacePayload.Actions(conversation, bindingIssuedAt);
        var revision = checked((state.CurrentSurfaces.FirstOrDefault(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal))?.SurfaceRevision ?? 0) + 1);
        var presentation = new SurfaceFeedPresentation(
            record.RequestId,
            "conversation",
            record.ConversationId,
            ConversationSurfacePayload.RequiredCapabilities,
            payload,
            conversation.Revision,
            SurfaceFeedPresentation.CurrentVersion);
        return new(
            record.EventId,
            ConversationSurfacePayload.HomeSurfaceId,
            revision,
            SurfaceContentHash.Compute(payload, descriptors),
            JsonSerializer.SerializeToUtf8Bytes(presentation),
            record.OccurredAt,
            null,
            CreateBindings(descriptors, revision));
    }

    private static SurfaceActionBinding[] CreateBindings(IReadOnlyList<StoredActionBinding> descriptors, int surfaceRevision) => descriptors.Select(descriptor => new SurfaceActionBinding(
            descriptor.BindingId,
            ConversationSurfacePayload.HomeSurfaceId,
            surfaceRevision,
            descriptor.ActionType,
            descriptor.InputSchemaRef,
            descriptor.RequiredGrant,
            descriptor.ActionSchemaVersion,
            Convert.ToHexStringLower(SHA256.HashData(RandomNumberGenerator.GetBytes(32))),
            descriptor.MaxUses,
            0,
            descriptor.ExpiresAt,
            null,
            null)).ToArray();

    private async Task StopReminderAsync()
    {
        _timer?.Dispose();
        _timer = null;
        _reminder ??= await this.GetReminder(ReminderName);
        if (_reminder is null) return;
        await this.UnregisterReminder(_reminder);
        _reminder = null;
    }

}
