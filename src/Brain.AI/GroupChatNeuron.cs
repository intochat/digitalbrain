namespace DigitalBrain.AI;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Streams;

[GrainType("chat.group.v1")]
public sealed class GroupChatNeuron(
    [FromKeyedServices("groupchat-receipts")] IDurableDictionary<string, CommandReceipt> receipts,
    [FromKeyedServices("groupchat-events")] IDurableDictionary<string, byte> processedEvents,
    [FromKeyedServices("groupchat-sequences")] IDurableDictionary<string, long> sourceSequences,
    [FromKeyedServices("groupchat-outbox")] IDurableList<OutboxIntent<GroupChatStepEvent>> outbox,
    [FromKeyedServices("groupchat-domain")] IDurableDictionary<string, string> domain,
    [FromKeyedServices("groupchat-flags")] IDurableDictionary<string, string> flags,
    [FromKeyedServices("groupchat-failures")] IDurableList<SanitizedFailure> failures,
    [FromKeyedServices("groupchat-accepted-causation")] IDurableDictionary<string, byte> acceptedCausation,
    [FromKeyedServices("groupchat-rejected-causation")] IDurableDictionary<string, byte> rejectedCausation,
    IOptions<AiProviderOptions> options) : ReactiveNeuron<GroupChatStepEvent>(
        receipts,
        processedEvents,
        sourceSequences,
        outbox,
        domain,
        flags,
        failures,
        acceptedCausation,
        rejectedCausation), IGroupChatControl
{
    public const string EventStreamNamespace = "groupchat.steps";
    public const string CancelActionId = "cancel";
    private const string StreamIdFlag = "stream-id";
    private const string SubscribedFlag = "stream-subscribed";
    private const string SourceSequenceFlag = "step-source-sequence";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AiProviderOptions _options = options.Value;
    private readonly OneStepGroupChatEngine _engine = new();
    private Guid _activationToken;

    protected override bool AutoDrainAfterCommit =>
        !Flags.TryGetValue("auto-drain", out var value) || value != "0";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationToken = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    protected override async Task OnReactiveActivateAsync(CancellationToken cancellationToken)
    {
        if (Flags.TryGetValue(SubscribedFlag, out var subscribed) && subscribed == "1")
            await RegisterStreamAsync();
    }

    public Task<CommandReceipt> StartDiscussionAsync(CommandSynapse<StartDiscussion> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            var discussionId = command.Metadata.CommandId;
            var streamId = CreateDeterministicStreamId(this.GetPrimaryKeyString(), discussionId);
            Flags[StreamIdFlag] = streamId.ToString("N");
            Flags[SubscribedFlag] = "1";
            Flags[SourceSequenceFlag] = "1";
            await RegisterStreamAsync();

            var state = new GroupChatDomainState(
                Topic: payload.Topic,
                GptKey: payload.GptKey,
                GrokKey: payload.GrokKey,
                DiscussionId: discussionId,
                ParticipantCursor: 0,
                StepCount: 0,
                IsCancelled: false,
                CheckpointSessionId: null,
                CheckpointId: null,
                CheckpointJson: null,
                Transcript:
                [
                    new TranscriptEntry("user", payload.Topic, null)
                ],
                Status: "active",
                FailureMessage: null);

            const long uiRevision = 1;
            var uiIntent = CreateUiCandidateIntent(
                command.Metadata,
                state,
                uiRevision,
                UiFeedCandidate.CreateSnapshot(new UiSurfaceSnapshot(
                    BuildSurface(ResolveSurfaceId(), state, uiRevision))));
            var stepEvent = CreateStepEventSynapse(
                command.Metadata,
                new GroupChatStepEvent(0, discussionId, GroupChatStepEvent.StepKind),
                sourceSequence: 1);
            var stepIntent = OutboxIntent<GroupChatStepEvent>.Create(
                EventStreamNamespace,
                streamId,
                stepEvent);

            await commit(new ReactiveCommit<GroupChatStepEvent>(
                JsonSerializer.Serialize(state, JsonOptions),
                UiRevision: uiRevision,
                Outbox: [uiIntent, stepIntent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command) =>
        ExecuteCommandCoreAsync(command, async (payload, commit) =>
        {
            EnsureExpectedUiRevision(payload.ExpectedRevision);
            if (!string.Equals(payload.ActionId, CancelActionId, StringComparison.Ordinal))
                throw new BrainException(BrainErrors.FailureSanitized, ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage);

            var state = ReadState() ?? throw new BrainException(BrainErrors.FailureSanitized, ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage);
            var cancelled = state with { IsCancelled = true, Status = "cancelled" };
            var nextUi = UiRevision + 1;
            var uiIntent = CreateUiCandidateIntent(
                command.Metadata,
                cancelled,
                nextUi,
                UiFeedCandidate.CreateSnapshot(new UiSurfaceSnapshot(
                    BuildSurface(ResolveSurfaceId(), cancelled, nextUi))));
            await commit(new ReactiveCommit<GroupChatStepEvent>(
                JsonSerializer.Serialize(cancelled, JsonOptions),
                UiRevision: nextUi,
                Outbox: [uiIntent]));
            return CommandReceiptStatus.Accepted;
        });

    public Task<UiSurfaceSnapshot> GetSurfaceAsync()
    {
        var state = ReadState();
        var surface = BuildSurface(ResolveSurfaceId(), state, UiRevision);
        return Task.FromResult(new UiSurfaceSnapshot(surface));
    }

    public Task<GroupChatDiagnosticsSnapshot> GetDiagnosticsAsync()
    {
        var state = ReadState();
        var checkpointJson = state?.CheckpointJson;
        return Task.FromResult(new GroupChatDiagnosticsSnapshot(
            TranscriptCount: state?.Transcript.Count ?? 0,
            ParticipantCursor: state?.ParticipantCursor ?? 0,
            StepCount: state?.StepCount ?? 0,
            CheckpointId: state?.CheckpointId,
            CheckpointSessionId: state?.CheckpointSessionId,
            IsCancelled: state?.IsCancelled ?? false,
            OutboxCount: Outbox.Count,
            UiRevision: UiRevision,
            Revision: CurrentRevision,
            ActivationToken: _activationToken,
            TranscriptTexts: state?.Transcript.Select(t => t.Text).ToArray() ?? [],
            LastFailureMessage: state?.FailureMessage,
            HasCheckpointJson: !string.IsNullOrWhiteSpace(checkpointJson),
            CheckpointJsonLength: checkpointJson?.Length ?? 0,
            SurfaceId: ResolveSurfaceId(),
            Topic: state?.Topic,
            GptKey: state?.GptKey,
            GrokKey: state?.GrokKey,
            Status: state?.Status ?? string.Empty));
    }

    public async Task SetAutoDrainAsync(bool enabled)
    {
        Flags["auto-drain"] = enabled ? "1" : "0";
        await WriteStateAsync(CancellationToken.None);
    }

    public Task DrainOutboxAsync() => DrainOutboxCoreAsync(throwOnPublishFailure: false);

    public Task RequestDeactivationAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public Task<EventSynapse<GroupChatStepEvent>?> PeekOutboxEventAsync() =>
        Task.FromResult(Outbox.Count == 0 ? null : Outbox[0].Event);

    public Task<EventSynapse<GroupChatStepEvent>?> PeekStepOutboxEventAsync()
    {
        for (var index = 0; index < Outbox.Count; index++)
        {
            var item = Outbox[index].Event;
            if (item.Payload.IsStepIntent)
                return Task.FromResult<EventSynapse<GroupChatStepEvent>?>(item);
        }

        return Task.FromResult<EventSynapse<GroupChatStepEvent>?>(null);
    }

    public Task PublishStepEventAsync(EventSynapse<GroupChatStepEvent> @event)
    {
        var stepPayload = @event.Payload with
        {
            IntentKind = GroupChatStepEvent.StepKind,
            Candidate = null
        };
        var stepEvent = @event with { Payload = stepPayload };
        var streamId = ReadStreamId();
        return PublishEventAsync(stepEvent, DefaultStreamProviderName, EventStreamNamespace, streamId);
    }

    public Task PublishUiCandidateEventAsync(EventSynapse<GroupChatStepEvent> @event)
    {
        if (@event.Payload.Candidate is null)
            throw new BrainException(BrainErrors.FailureSanitized, ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage);

        return PublishUiFeedCandidateAsync(@event.Metadata, @event.Payload.Candidate);
    }

    protected override async Task PublishOutboxIntentAsync(OutboxIntent<GroupChatStepEvent> intent)
    {
        if (intent.Event.Payload.IsUiIntent)
        {
            var candidate = intent.Event.Payload.Candidate
                ?? throw new BrainException(BrainErrors.FailureSanitized, ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage);
            await PublishUiFeedCandidateAsync(intent.Event.Metadata, candidate);
            return;
        }

        var stepPayload = intent.Event.Payload with
        {
            IntentKind = GroupChatStepEvent.StepKind,
            Candidate = null
        };
        var stepEvent = intent.Event with { Payload = stepPayload };
        await PublishEventAsync(stepEvent, DefaultStreamProviderName, intent.StreamNamespace, intent.StreamId);
    }

    private async Task OnStreamEventAsync(EventSynapse<GroupChatStepEvent> item, StreamSequenceToken? token)
    {
        await HandleEventCoreAsync(item, async (payload, commit) =>
        {
            if (payload.IsUiIntent)
            {
                await commit(new ReactiveCommit<GroupChatStepEvent>(DomainState, UiRevision, Outbox: []));
                return;
            }

            var state = ReadState();
            if (state is null)
            {
                await commit(new ReactiveCommit<GroupChatStepEvent>(DomainState, UiRevision, Outbox: []));
                return;
            }

            if (state.IsCancelled || string.Equals(state.Status, "failed", StringComparison.Ordinal))
            {
                await commit(new ReactiveCommit<GroupChatStepEvent>(
                    JsonSerializer.Serialize(state, JsonOptions),
                    UiRevision,
                    Outbox: []));
                return;
            }

            if (state.StepCount >= _options.MaximumDiscussionSteps)
            {
                var completed = state with { Status = "completed" };
                var nextUi = UiRevision + 1;
                var completeIntent = CreateUiCandidateIntent(
                    item.Metadata,
                    completed,
                    nextUi,
                    UiFeedCandidate.CreateSnapshot(new UiSurfaceSnapshot(
                        BuildSurface(ResolveSurfaceId(), completed, nextUi))));
                await commit(new ReactiveCommit<GroupChatStepEvent>(
                    JsonSerializer.Serialize(completed, JsonOptions),
                    nextUi,
                    Outbox: [completeIntent]));
                return;
            }

            try
            {
                var (gptAgent, grokAgent) = CreateParticipantAgents(state);
                var transcript = state.Transcript.Select(ToChatMessage).ToList();
                var step = await _engine.AdvanceAsync(
                    transcript,
                    state.ParticipantCursor,
                    gptAgent,
                    grokAgent,
                    state.CheckpointSessionId,
                    state.CheckpointId,
                    state.CheckpointJson);

                var response = step.ParticipantResponses[0];
                var nextTranscript = MergeTranscript(state.Transcript, step.TerminalTranscript, response);
                var next = state with
                {
                    Transcript = nextTranscript,
                    ParticipantCursor = (state.ParticipantCursor + 1) % 2,
                    StepCount = state.StepCount + 1,
                    CheckpointSessionId = step.Checkpoint.SessionId,
                    CheckpointId = step.Checkpoint.CheckpointId,
                    CheckpointJson = step.CheckpointJson,
                    Status = nextTranscript.Count > 0 && state.StepCount + 1 >= _options.MaximumDiscussionSteps
                        ? "completed"
                        : "active",
                    FailureMessage = null
                };
                if (next.StepCount >= _options.MaximumDiscussionSteps)
                    next = next with { Status = "completed" };

                var nextUi = UiRevision + 1;
                var intents = new List<OutboxIntent<GroupChatStepEvent>>
                {
                    CreateUiCandidateIntent(
                        item.Metadata,
                        next,
                        nextUi,
                        UiFeedCandidate.CreateSnapshot(new UiSurfaceSnapshot(
                            BuildSurface(ResolveSurfaceId(), next, nextUi))))
                };

                if (!next.IsCancelled && next.StepCount < _options.MaximumDiscussionSteps)
                {
                    var nextSequence = ReadSourceSequence() + 1;
                    Flags[SourceSequenceFlag] = nextSequence.ToString();
                    var streamId = ReadStreamId();
                    var stepEvent = CreateStepEventSynapse(
                        item.Metadata,
                        new GroupChatStepEvent(next.StepCount, next.DiscussionId, GroupChatStepEvent.StepKind),
                        nextSequence);
                    intents.Add(OutboxIntent<GroupChatStepEvent>.Create(
                        EventStreamNamespace,
                        streamId,
                        stepEvent));
                }

                await commit(new ReactiveCommit<GroupChatStepEvent>(
                    JsonSerializer.Serialize(next, JsonOptions),
                    UiRevision: nextUi,
                    Outbox: intents));
            }
            catch (BrainException ex) when (ex.Code == BrainErrors.FailureSanitized)
            {
                await CommitFailureAsync(state, item.Metadata, commit);
            }
            catch (OperationCanceledException)
            {
                await CommitFailureAsync(state, item.Metadata, commit);
            }
            catch (Exception)
            {
                await CommitFailureAsync(state, item.Metadata, commit);
            }
        });
    }

    private async Task CommitFailureAsync(
        GroupChatDomainState state,
        SynapseMetadata metadata,
        CommitReactionAsync<GroupChatStepEvent> commit)
    {
        var failed = state with
        {
            Status = "failed",
            FailureMessage = ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage
        };
        var nextUi = UiRevision + 1;
        var uiIntent = CreateUiCandidateIntent(
            metadata,
            failed,
            nextUi,
            UiFeedCandidate.CreateFailure(BrainErrors.FailureSanitized));
        await commit(new ReactiveCommit<GroupChatStepEvent>(
            JsonSerializer.Serialize(failed, JsonOptions),
            UiRevision: nextUi,
            Outbox: [uiIntent]));
    }

    private (AIAgent Gpt, AIAgent Grok) CreateParticipantAgents(GroupChatDomainState state)
    {
        var self = NeuronAddress.Parse(this.GetPrimaryKeyString());
        var gpt = GrainFactory.GetGrain<IGpt56Turn>(state.GptKey);
        var grok = GrainFactory.GetGrain<IGrok45Turn>(state.GrokKey);

        SynapseMetadata MetadataFactory()
        {
            var id = Guid.NewGuid();
            return new SynapseMetadata(
                CommandId: id,
                EventId: id,
                CausationId: id,
                CorrelationId: id,
                OrganizationId: self.OrganizationId,
                PrincipalId: new PrincipalId("group-chat"),
                SpaceId: self.SpaceId,
                Source: self,
                SourceSequence: 0,
                CausalDepth: 0,
                OccurredAt: DateTimeOffset.UtcNow);
        }

        var gptClient = new ParticipantGrainChatClient(
            "gpt56",
            command => gpt.CompleteTurnAsync(command),
            MetadataFactory);
        var grokClient = new ParticipantGrainChatClient(
            "grok45",
            command => grok.CompleteTurnAsync(command),
            MetadataFactory);

        AIAgent gptAgent = new ChatClientAgent(gptClient, name: "gpt56");
        AIAgent grokAgent = new ChatClientAgent(grokClient, name: "grok45");
        return (gptAgent, grokAgent);
    }

    private async Task RegisterStreamAsync()
    {
        if (!Flags.TryGetValue(StreamIdFlag, out var raw) || !Guid.TryParse(raw, out var streamId))
            return;

        await RegisterEventSubscriptionAsync(
            DefaultStreamProviderName,
            EventStreamNamespace,
            streamId,
            OnStreamEventAsync);
    }

    private OutboxIntent<GroupChatStepEvent> CreateUiCandidateIntent(
        SynapseMetadata source,
        GroupChatDomainState state,
        long uiRevision,
        UiFeedCandidate candidate)
    {
        var eventId = CreateCandidateEventId(state.DiscussionId, uiRevision);
        var self = NeuronAddress.Parse(this.GetPrimaryKeyString());
        var metadata = new SynapseMetadata(
            CommandId: source.CommandId,
            EventId: eventId,
            CausationId: source.EventId == Guid.Empty ? source.CommandId : source.EventId,
            CorrelationId: source.CorrelationId,
            OrganizationId: source.OrganizationId,
            PrincipalId: source.PrincipalId,
            SpaceId: source.SpaceId,
            Source: self,
            SourceSequence: uiRevision,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);
        var payload = new GroupChatStepEvent(0, state.DiscussionId, GroupChatStepEvent.UiKind, candidate);
        var @event = new EventSynapse<GroupChatStepEvent>(metadata, payload);
        return OutboxIntent<GroupChatStepEvent>.Create(
            UiFeedStreams.CandidateNamespace,
            UiFeedStreams.StreamId(source.OrganizationId, source.SpaceId),
            @event);
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

    private EventSynapse<GroupChatStepEvent> CreateStepEventSynapse(
        SynapseMetadata source,
        GroupChatStepEvent payload,
        long sourceSequence)
    {
        var eventId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var metadata = new SynapseMetadata(
            CommandId: source.CommandId,
            EventId: eventId,
            CausationId: causationId,
            CorrelationId: source.CorrelationId,
            OrganizationId: source.OrganizationId,
            PrincipalId: source.PrincipalId,
            SpaceId: source.SpaceId,
            Source: NeuronAddress.Parse(this.GetPrimaryKeyString()),
            SourceSequence: sourceSequence,
            CausalDepth: 0,
            OccurredAt: DateTimeOffset.UtcNow);
        return new EventSynapse<GroupChatStepEvent>(metadata, payload with
        {
            IntentKind = GroupChatStepEvent.StepKind,
            Candidate = null
        });
    }

    private GroupChatDomainState? ReadState()
    {
        if (string.IsNullOrWhiteSpace(DomainState))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GroupChatDomainState>(DomainState, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private long ReadSourceSequence() =>
        Flags.TryGetValue(SourceSequenceFlag, out var raw) && long.TryParse(raw, out var value) ? value : 0;

    private Guid ReadStreamId()
    {
        if (Flags.TryGetValue(StreamIdFlag, out var raw) && Guid.TryParse(raw, out var streamId))
            return streamId;
        throw new BrainException(BrainErrors.FailureSanitized, ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage);
    }

    private string ResolveSurfaceId() => this.GetPrimaryKeyString();

    private static UiSurface BuildSurface(string surfaceId, GroupChatDomainState? state, long uiRevision)
    {
        if (state is null)
            return new UiSurface(surfaceId, uiRevision, []);

        var blocks = new List<UiBlock>
        {
            new("topic", state.Topic, []),
            new("status", state.Status, state.IsCancelled
                ? []
                : [new UiAction(CancelActionId, "Cancel", uiRevision)])
        };

        foreach (var entry in state.Transcript)
        {
            var label = string.IsNullOrWhiteSpace(entry.Author) ? entry.Role : $"{entry.Role}:{entry.Author}";
            blocks.Add(new UiBlock("message", $"{label}|{entry.Text}", []));
        }

        if (!string.IsNullOrWhiteSpace(state.CheckpointId))
            blocks.Add(new UiBlock("checkpoint", state.CheckpointId, []));

        if (!string.IsNullOrWhiteSpace(state.FailureMessage))
            blocks.Add(new UiBlock("failure", state.FailureMessage, []));

        return new UiSurface(surfaceId, uiRevision, blocks);
    }

    private static List<TranscriptEntry> MergeTranscript(
        IReadOnlyList<TranscriptEntry> current,
        List<ChatMessage>? terminal,
        ChatMessage response)
    {
        var result = current.ToList();
        if (terminal is { Count: > 0 })
        {
            result = terminal.Select(m => new TranscriptEntry(
                m.Role.Value,
                m.Text ?? string.Empty,
                m.AuthorName)).ToList();
        }

        if (!result.Any(e => e.Text == response.Text && e.Role == response.Role.Value && e.Author == response.AuthorName))
        {
            result.Add(new TranscriptEntry(response.Role.Value, response.Text ?? string.Empty, response.AuthorName));
        }

        return result;
    }

    private static ChatMessage ToChatMessage(TranscriptEntry entry) =>
        new(new ChatRole(entry.Role), entry.Text)
        {
            AuthorName = entry.Author
        };

    private static Guid CreateDeterministicStreamId(string grainKey, Guid discussionId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{grainKey}:{discussionId:N}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static Guid CreateCandidateEventId(Guid discussionId, long uiRevision)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"groupchat.ui\n{discussionId:N}\n{uiRevision}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed record GroupChatDomainState(
        string Topic,
        string GptKey,
        string GrokKey,
        Guid DiscussionId,
        int ParticipantCursor,
        int StepCount,
        bool IsCancelled,
        string? CheckpointSessionId,
        string? CheckpointId,
        string? CheckpointJson,
        IReadOnlyList<TranscriptEntry> Transcript,
        string Status,
        string? FailureMessage);

    private sealed record TranscriptEntry(string Role, string Text, string? Author);
}
