namespace DigitalBrain.AI;

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

            var stepEvent = CreateStepEventSynapse(
                command.Metadata,
                new GroupChatStepEvent(0, discussionId),
                sourceSequence: 1);

            var intent = OutboxIntent<GroupChatStepEvent>.Create(
                EventStreamNamespace,
                streamId,
                stepEvent);

            await commit(new ReactiveCommit<GroupChatStepEvent>(
                JsonSerializer.Serialize(state, JsonOptions),
                UiRevision: 1,
                Outbox: [intent]));
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
            await commit(new ReactiveCommit<GroupChatStepEvent>(
                JsonSerializer.Serialize(cancelled, JsonOptions),
                UiRevision: UiRevision + 1,
                Outbox: []));
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

    public Task PublishStepEventAsync(EventSynapse<GroupChatStepEvent> @event)
    {
        var streamId = ReadStreamId();
        return PublishEventAsync(@event, DefaultStreamProviderName, EventStreamNamespace, streamId);
    }

    private async Task OnStreamEventAsync(EventSynapse<GroupChatStepEvent> item, StreamSequenceToken? token)
    {
        await HandleEventCoreAsync(item, async (payload, commit) =>
        {
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
                await commit(new ReactiveCommit<GroupChatStepEvent>(
                    JsonSerializer.Serialize(completed, JsonOptions),
                    UiRevision,
                    Outbox: []));
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
                    Status = "active",
                    FailureMessage = null
                };

                var intents = new List<OutboxIntent<GroupChatStepEvent>>();
                if (!next.IsCancelled && next.StepCount < _options.MaximumDiscussionSteps)
                {
                    var nextSequence = ReadSourceSequence() + 1;
                    Flags[SourceSequenceFlag] = nextSequence.ToString();
                    var streamId = ReadStreamId();
                    var stepEvent = CreateStepEventSynapse(
                        item.Metadata,
                        new GroupChatStepEvent(next.StepCount, next.DiscussionId),
                        nextSequence);
                    intents.Add(OutboxIntent<GroupChatStepEvent>.Create(
                        EventStreamNamespace,
                        streamId,
                        stepEvent));
                }

                await commit(new ReactiveCommit<GroupChatStepEvent>(
                    JsonSerializer.Serialize(next, JsonOptions),
                    UiRevision: UiRevision + 1,
                    Outbox: intents));
            }
            catch (BrainException ex) when (ex.Code == BrainErrors.FailureSanitized)
            {
                var failed = state with
                {
                    Status = "failed",
                    FailureMessage = ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage
                };
                await commit(new ReactiveCommit<GroupChatStepEvent>(
                    JsonSerializer.Serialize(failed, JsonOptions),
                    UiRevision: UiRevision + 1,
                    Outbox: []));
            }
            catch (OperationCanceledException)
            {
                var failed = state with
                {
                    Status = "failed",
                    FailureMessage = ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage
                };
                await commit(new ReactiveCommit<GroupChatStepEvent>(
                    JsonSerializer.Serialize(failed, JsonOptions),
                    UiRevision: UiRevision + 1,
                    Outbox: []));
            }
            catch (Exception)
            {
                var failed = state with
                {
                    Status = "failed",
                    FailureMessage = ReactiveNeuronPipeline<GroupChatStepEvent>.UnknownFailureMessage
                };
                await commit(new ReactiveCommit<GroupChatStepEvent>(
                    JsonSerializer.Serialize(failed, JsonOptions),
                    UiRevision: UiRevision + 1,
                    Outbox: []));
            }
        });
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
        return new EventSynapse<GroupChatStepEvent>(metadata, payload);
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
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{grainKey}:{discussionId:N}"));
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
