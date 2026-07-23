using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;

namespace DigitalBrain.AI;

[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "GroupChat is the ratified public orchestration vocabulary.")]
public abstract class GroupChat : Neuron, IGroupChat, IWorkflowRunOwner, IWorkflowRunCompletion, IRemindable
{
    private const string ClockName = "ai.group-chat.clock";
    private const string ProtectionPurpose = "DigitalBrain.AI.GroupChat.AgentSession.v1";
    private const string RecoveryReminderName = "db.ai.workflow-run";
    private const string StateName = "ai.group-chat.session";
    private const string WorkerStateName = "ai.group-chat.worker";
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly IDurableValue<byte[]> _workerState;
    private readonly Serializer<AIWorkerState> _workerStates;
    private readonly Serializer<ChatMessage> _messages;
    private readonly TimeProvider _clock;
    private IGrainTimer? _runnerDispatch;

    protected GroupChat()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _workerState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(WorkerStateName);
        _workerStates = ServiceProvider.GetRequiredService<Serializer<AIWorkerState>>();
        _messages = ServiceProvider.GetRequiredService<Serializer<ChatMessage>>();
        _clock = ServiceProvider.GetKeyedService<TimeProvider>(ClockName)
            ?? ServiceProvider.GetService<TimeProvider>()
            ?? TimeProvider.System;
    }

    protected abstract IReadOnlyList<Participant> Participants { get; }

    protected abstract IReadOnlyList<ChatMessage> CreateMessages(Goal goal);

    protected abstract Result CreateResult(IReadOnlyList<ChatMessage> messages);

    protected Participant<TNeuron> Participant<TNeuron>(string? name = null)
        where TNeuron : INeuron
        => new(NeuronId.For<TNeuron>(Id.Owner, name ?? Id.Name));

    public async Task AcceptAsync(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        ValidateCapabilityCaller(request.Task);

        var cursor = new AttemptCursor(
            request.Task,
            request.Worker,
            request.Attempt,
            request.Revision);
        var existing = LoadWorkerState();

        if (existing is not null)
        {
            if (existing.Cursor == cursor)
            {
                return;
            }

            if (existing.ActiveRun is not null)
            {
                throw new InvalidOperationException(
                    $"GroupChat '{Id}' already has an active supervised Attempt.");
            }

        }

        var participantSnapshot = (Participants
            ?? throw new InvalidOperationException("Participants returned null.")).ToArray();

        if (participantSnapshot.Length == 0)
        {
            throw new InvalidOperationException("A GroupChat worker requires at least one participant.");
        }

        if (participantSnapshot.Any(participant => participant is null || participant.Id.Owner != Id.Owner))
        {
            throw new InvalidOperationException(
                $"Every GroupChat participant must belong to worker '{Id}'s owner.");
        }

        var definition = SessionCompatibility.Describe(GetType(), participantSnapshot);
        var replayInput = ChatMessageCopies.Clone(
            CreateMessages(request.Goal)
            ?? throw new InvalidOperationException("CreateMessages returned null."),
            _messages);
        var run = new WorkflowRun(
            Guid.NewGuid(),
            cursor,
            definition.Fingerprint,
            InputCheckpoint: null,
            _clock.GetUtcNow() + RecoveryInterval);
        var causation = CaptureCapabilityCausation(request.Task);
        var state = new AIWorkerState(
            cursor,
            replayInput,
            definition,
            Checkpoint: null,
            causation,
            run);

        await this.RegisterOrUpdateReminder(
            RecoveryReminderName,
            RecoveryInterval,
            RecoveryInterval);
        StageWorkerState(state);
        await ReplyAsync(new AttemptAccepted(
            cursor.Task,
            cursor.Worker,
            cursor.Attempt,
            cursor.Revision));
        Schedule(state);
    }

    public async Task ContinueAsync(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ValidateCursor(cursor);
        ValidateCapabilityCaller(cursor.Task);

        var state = LoadWorkerState()
            ?? throw new InvalidOperationException(
                $"GroupChat '{Id}' has no supervised Attempt state.");

        if (cursor == state.Cursor)
        {
            return;
        }

        var expected = state.Cursor with
        {
            Revision = checked(state.Cursor.Revision + 1),
        };

        if (cursor != expected)
        {
            throw new InvalidOperationException(
                $"Attempt cursor '{cursor}' is not GroupChat '{Id}'s next cursor '{expected}'.");
        }

        if (state.ActiveRun is not null)
        {
            throw new InvalidOperationException(
                $"GroupChat '{Id}' cannot continue before its active run is adopted.");
        }

        if (state.Checkpoint is null)
        {
            throw new InvalidOperationException(
                $"GroupChat '{Id}' cannot continue without an adopted checkpoint.");
        }

        var run = new WorkflowRun(
            Guid.NewGuid(),
            cursor,
            state.Definition.Fingerprint,
            state.Checkpoint,
            _clock.GetUtcNow() + RecoveryInterval);
        var resumed = state with
        {
            Cursor = cursor,
            Causation = CaptureCapabilityCausation(cursor.Task),
            ActiveRun = run,
        };

        await this.RegisterOrUpdateReminder(
            RecoveryReminderName,
            RecoveryInterval,
            RecoveryInterval);
        StageWorkerState(resumed);
        Schedule(resumed);
    }

    public async Task CancelAsync(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ValidateCursor(cursor);
        ValidateCapabilityCaller(cursor.Task);

        var state = LoadWorkerState()
            ?? throw new InvalidOperationException(
                $"GroupChat '{Id}' has no supervised Attempt state.");

        if (cursor != state.Cursor)
        {
            throw new InvalidOperationException(
                $"Attempt cursor '{cursor}' does not match GroupChat '{Id}'s current cursor '{state.Cursor}'.");
        }

        if (state.ActiveRun is null)
        {
            return;
        }

        if (state.ActiveRun.Cursor != cursor)
        {
            throw new InvalidOperationException(
                $"GroupChat '{Id}'s active run does not match its persisted Attempt cursor.");
        }

        StageWorkerState(state with { ActiveRun = null });
        await ReplyAsync(
            state.Causation,
            new AttemptCancelled(
                cursor.Task,
                cursor.Worker,
                cursor.Attempt,
                cursor.Revision));
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, RecoveryReminderName, StringComparison.Ordinal))
        {
            await base.ReceiveReminder(reminderName, status);
            return;
        }

        var state = LoadWorkerState();

        if (state?.ActiveRun is not { } active)
        {
            await UnregisterRecoveryReminderAsync();
            return;
        }

        var now = _clock.GetUtcNow();

        if (now < active.RecoverAfterUtc)
        {
            return;
        }

        var replacement = active with
        {
            RunId = Guid.NewGuid(),
            RecoverAfterUtc = now + RecoveryInterval,
        };
        var replacementState = state with { ActiveRun = replacement };

        await SaveWorkerStateAsync(replacementState);
        await DispatchAsync(replacementState);
    }

    public async Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (LoadWorkerState()?.ActiveRun is not null)
        {
            throw new InvalidOperationException(
                $"GroupChat '{Id}' cannot run a direct chat turn while a supervised Attempt is active.");
        }

        var snapshot = Participants.ToArray();
        var definition = SessionCompatibility.Describe(GetType(), snapshot);
        var protector = ServiceProvider.GetRequiredService<IDurablePayloadProtector>();
        var protectionPurpose = SessionProtectionPurpose(Id, definition.Fingerprint);
        var turnScheduler = TaskScheduler.Current;
        var participants = MafParticipantAdapter.CreateAll(GrainFactory, snapshot, turnScheduler);
        var workflow = GroupChatWorkflow.Create(participants);
        var agent = workflow.AsAIAgent(
            id: definition.HostId,
            name: definition.HostName,
            description: null,
            executionEnvironment: InProcessExecution.Lockstep,
            includeExceptionDetails: false,
            includeWorkflowOutputsInResponse: false);
        var session = _state.Value is { Length: > 0 } serialized
            ? await RestoreAsync(agent, serialized, definition, protector, protectionPurpose)
            : await agent.CreateSessionAsync();
        var response = await agent.RunAsync(messages, session);
        var serializedSession = await agent.SerializeSessionAsync(session);
        var protectedSession = protector.Protect(
            protectionPurpose,
            Encoding.UTF8.GetBytes(serializedSession.GetRawText()));
        var envelope = new OrchestrationState(
            definition.FormatVersion,
            definition.MafVersion,
            definition.Fingerprint,
            definition.Participants,
            protectedSession);

        _state.Value = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await WriteStateAsync();

        return response.AsChatResponse();
    }

    async Task<CapabilityDelegation> IWorkflowRunOwner.AuthorizeParticipantAsync(
        WorkflowRun run,
        OrchestrationParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(participant);
        var state = RequireActive(run);
        var storedParticipant = AssertParticipant(state.Definition, participant);
        var contract = Type.GetType(storedParticipant.Contract, throwOnError: true)
            ?? throw new InvalidOperationException(
                $"Participant contract '{storedParticipant.Contract}' cannot be resolved.");
        var invocationContract = typeof(ILLM).IsAssignableFrom(contract)
            ? typeof(ILLM)
            : typeof(IAgent).IsAssignableFrom(contract)
                ? typeof(IAgent)
                : throw new InvalidOperationException(
                    $"Participant contract '{contract.FullName}' is not an AI participant.");

        return await DelegateCapabilityAsync(
            state.Causation,
            Runner(run).GetGrainId(),
            storedParticipant.NeuronId,
            invocationContract,
            nameof(IAgent.RespondAsync));
    }

    async Task<CapabilityDelegation> IWorkflowRunOwner.AuthorizeCompletionAsync(WorkflowRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var state = RequireActive(run);

        return await DelegateCapabilityAsync(
            state.Causation,
            Runner(run).GetGrainId(),
            Id,
            typeof(IWorkflowRunCompletion),
            nameof(IWorkflowRunCompletion.CompleteAsync));
    }

    async Task<bool> IWorkflowRunCompletion.CompleteAsync(WorkflowRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var state = RequireActive(result.Run);

        var identity = WorkflowCheckpointIdentity.For(state.Cursor);

        if (!string.Equals(
                result.OutputCheckpoint.SessionId,
                identity.SessionId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The returned checkpoint does not belong to the active Attempt lineage.");
        }

        var checkpointGrain = GrainFactory.GetGrain<IWorkflowCheckpointGrain>(
            IdSpan.Create(identity.GrainKey));
        var children = await checkpointGrain.IndexAsync(state.ActiveRun!.InputCheckpoint);

        if (!children.Contains(result.OutputCheckpoint))
        {
            throw new InvalidOperationException(
                "The returned checkpoint is not an exact child of the active input checkpoint lineage.");
        }

        _ = await checkpointGrain.ReadAsync(result.OutputCheckpoint);

        var adopted = state with
        {
            Checkpoint = result.OutputCheckpoint,
            ActiveRun = null,
        };

        if (result.TerminalMessages is null)
        {
            StageWorkerState(adopted);
            await ReplyAsync(
                state.Causation,
                new AttemptAdvanced(
                    state.Cursor.Task,
                    state.Cursor.Worker,
                    state.Cursor.Attempt,
                    state.Cursor.Revision));
            return true;
        }

        var terminalMessages = Array.AsReadOnly(ChatMessageCopies.Clone(result.TerminalMessages, _messages));
        var mapped = CreateResult(terminalMessages)
            ?? throw new InvalidOperationException("CreateResult returned null.");
        StageWorkerState(adopted);
        await ReplyAsync(
            state.Causation,
            new AttemptSucceeded(
                state.Cursor.Task,
                state.Cursor.Worker,
                state.Cursor.Attempt,
                state.Cursor.Revision,
                mapped,
                []));
        return true;
    }

    private AIWorkerState RequireActive(WorkflowRun run)
    {
        var state = LoadWorkerState()
            ?? throw new InvalidOperationException(
                $"GroupChat '{Id}' has no supervised Attempt state.");

        if (!MatchesActive(state, run))
        {
            throw new InvalidOperationException(
                $"Workflow run '{run.RunId}' does not match GroupChat '{Id}'s active run fence.");
        }

        return state;
    }

    private static bool MatchesActive(AIWorkerState state, WorkflowRun run)
        => state.ActiveRun is { } active
            && active.RunId == run.RunId
            && active.Cursor == run.Cursor
            && string.Equals(
                active.DefinitionFingerprint,
                run.DefinitionFingerprint,
                StringComparison.Ordinal)
            && active.InputCheckpoint == run.InputCheckpoint
            && state.Cursor == run.Cursor
            && string.Equals(
                state.Definition.Fingerprint,
                run.DefinitionFingerprint,
                StringComparison.Ordinal)
            && state.Checkpoint == run.InputCheckpoint;

    private static OrchestrationParticipant AssertParticipant(
        OrchestrationDefinition definition,
        OrchestrationParticipant requested)
    {
        var matches = definition.Participants
            .Where(participant => participant == requested)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                "The requested participant is not an exact member of the active definition snapshot.");
    }

    private void Schedule(AIWorkerState state)
    {
        _runnerDispatch?.Dispose();
        _runnerDispatch = RegisterGrainTimer(
            async () =>
            {
                _runnerDispatch?.Dispose();
                _runnerDispatch = null;

                await DispatchAsync(state);
            },
            new GrainTimerCreationOptions(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
    }

    private async Task DispatchAsync(AIWorkerState state)
    {
        if (state.ActiveRun is not { } run)
        {
            return;
        }

        var current = LoadWorkerState();

        if (current is null || !MatchesActive(current, run))
        {
            return;
        }

        var command = new WorkflowRunCommand(
            run,
            state.Definition,
            ChatMessageCopies.Clone(state.ReplayInput, _messages));

        await Runner(run).ExecuteAsync(command);
    }

    private IWorkflowRunner Runner(WorkflowRun run)
        => GrainFactory.GetGrain<IWorkflowRunner>(
            IdSpan.Create(WorkflowRunnerIdentity.GrainKey(run)));

    private AIWorkerState? LoadWorkerState()
        => _workerState.Value is { Length: > 0 } serialized
            ? _workerStates.Deserialize(serialized)
            : null;

    private void StageWorkerState(AIWorkerState state)
    {
        var previous = _workerState.Value?.ToArray();

        EnlistTurnRollback(() => _workerState.Value = previous);
        _workerState.Value = _workerStates.SerializeToArray(state);
    }

    private async Task SaveWorkerStateAsync(AIWorkerState state)
    {
        var previous = _workerState.Value?.ToArray();

        _workerState.Value = _workerStates.SerializeToArray(state);

        try
        {
            await WriteStateAsync();
        }
        catch
        {
            _workerState.Value = previous;

            throw;
        }
    }

    private async Task UnregisterRecoveryReminderAsync()
    {
        if (await this.GetReminder(RecoveryReminderName) is { } reminder)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    private static void ValidateShape(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Goal);

        if (request.Task == default)
        {
            throw new ArgumentException("An Attempt Task is required.", nameof(request));
        }

        if (request.Task != NeuronId.For<ITask>(request.Task.Owner, request.Task.Name))
        {
            throw new InvalidOperationException(
                $"Attempt Task '{request.Task}' is not a canonical Task neuron.");
        }

        if (request.Worker == default)
        {
            throw new ArgumentException("An Attempt Worker is required.", nameof(request));
        }

        if (request.Attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("An Attempt id is required.", nameof(request));
        }

        if (request.Revision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "An Attempt revision cannot be negative.");
        }
    }

    private void ValidateRequest(AttemptRequest request)
    {
        ValidateShape(request);

        if (request.Worker != Id)
        {
            throw new InvalidOperationException(
                $"Attempt Worker '{request.Worker}' does not match GroupChat '{Id}'.");
        }

        if (request.Task.Owner != Id.Owner)
        {
            throw new InvalidOperationException(
                $"Attempt Task '{request.Task}' does not belong to GroupChat '{Id}'s owner.");
        }
    }

    private void ValidateCursor(AttemptCursor cursor)
    {
        if (cursor.Task == default)
        {
            throw new ArgumentException("An Attempt Task is required.", nameof(cursor));
        }

        if (cursor.Task != NeuronId.For<ITask>(cursor.Task.Owner, cursor.Task.Name))
        {
            throw new InvalidOperationException(
                $"Attempt Task '{cursor.Task}' is not a canonical Task neuron.");
        }

        if (cursor.Worker == default)
        {
            throw new ArgumentException("An Attempt Worker is required.", nameof(cursor));
        }

        if (cursor.Worker != Id)
        {
            throw new InvalidOperationException(
                $"Attempt Worker '{cursor.Worker}' does not match GroupChat '{Id}'.");
        }

        if (cursor.Task.Owner != Id.Owner)
        {
            throw new InvalidOperationException(
                $"Attempt Task '{cursor.Task}' does not belong to GroupChat '{Id}'s owner.");
        }

        if (cursor.Attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("An Attempt id is required.", nameof(cursor));
        }

        if (cursor.Revision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cursor),
                "An Attempt revision cannot be negative.");
        }
    }

    private static async Task<AgentSession> RestoreAsync(
        AIAgent agent,
        byte[] serialized,
        OrchestrationDefinition definition,
        IDurablePayloadProtector protector,
        string protectionPurpose)
    {
        OrchestrationState stored;

        try
        {
            stored = JsonSerializer.Deserialize<OrchestrationState>(serialized)
                ?? throw RecoveryRequired();
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException)
        {
            throw RecoveryRequired(failure);
        }

        SessionCompatibility.RequireMatch(stored, definition);

        try
        {
            var sessionBytes = protector.Unprotect(protectionPurpose, stored.ProtectedSession);
            using var sessionJson = JsonDocument.Parse(sessionBytes);

            return await agent.DeserializeSessionAsync(sessionJson.RootElement.Clone());
        }
        catch (Exception failure) when (failure is CryptographicException
            or JsonException
            or FormatException
            or InvalidOperationException)
        {
            throw RecoveryRequired(failure);
        }
    }

    private static string SessionProtectionPurpose(NeuronId id, string definitionFingerprint)
        => $"{ProtectionPurpose}\n{id}\n{definitionFingerprint}";

    private static InvalidOperationException RecoveryRequired(Exception? failure = null)
        => new(
            "The durable group-chat session cannot be restored; an explicit migration or reset is required.",
            failure);
}
