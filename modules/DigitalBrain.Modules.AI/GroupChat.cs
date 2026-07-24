using System.Diagnostics.CodeAnalysis;
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
    private const string RecoveryReminderName = "db.ai.workflow-run";
    private const string StateName = "ai.group-chat.session";
    private const string WorkerStateName = "ai.group-chat.worker";
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(1);

    private readonly DirectAgentSession _directSession;
    private readonly IDurableValue<byte[]> _workerState;
    private readonly Serializer<AIWorkerState> _workerStates;
    private readonly Serializer<ChatMessage> _messages;
    private readonly TimeProvider _clock;
    private IGrainTimer? _runnerDispatch;

    protected GroupChat()
    {
        _directSession = new DirectAgentSession(
            ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName),
            ServiceProvider.GetRequiredService<IDurablePayloadProtector>(),
            () => WriteStateAsync(),
            Id);
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
            RequireCurrentDefinition(existing);

            if (existing.Cursor == cursor)
            {
                return;
            }

            if (existing.ActiveRun is not null
                || !existing.Lifecycle.AllowsDirect())
            {
                throw new InvalidOperationException(
                    $"GroupChat '{Id}' already has an active supervised Attempt.");
            }

        }

        var definition = CurrentSupervisedDefinition();
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
            run,
            SupervisedAttemptLifecycle.Running);

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

        var state = RequireCurrentDefinition(LoadWorkerState()
            ?? throw new InvalidOperationException(
                $"GroupChat '{Id}' has no supervised Attempt state."));

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

        if (!state.Lifecycle.CanContinue())
        {
            throw new InvalidOperationException(
                $"GroupChat '{Id}' cannot continue a supervised Attempt in lifecycle '{state.Lifecycle}'.");
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
            Lifecycle = SupervisedAttemptLifecycle.Running,
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

        if (state.Lifecycle is SupervisedAttemptLifecycle.Succeeded
            or SupervisedAttemptLifecycle.Cancelled)
        {
            return;
        }

        if (state.ActiveRun is { } active
            && active.Cursor != cursor)
        {
            throw new InvalidOperationException(
                $"GroupChat '{Id}'s active run does not match its persisted Attempt cursor.");
        }

        if (state.Lifecycle != SupervisedAttemptLifecycle.Cancelling)
        {
            state = state with
            {
                Lifecycle = SupervisedAttemptLifecycle.Cancelling,
            };
            await SaveWorkerStateAsync(state);
        }

        if (state.ActiveRun is { } activeRun)
        {
            await TryCancelRunnerAsync(activeRun);
        }

        StageWorkerState(state with
        {
            ActiveRun = null,
            Lifecycle = SupervisedAttemptLifecycle.Cancelled,
        });
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

        if (state.Lifecycle == SupervisedAttemptLifecycle.Cancelling)
        {
            await TryCancelRunnerAsync(active);
            return;
        }

        RequireCurrentDefinition(state);
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

        var worker = LoadWorkerState();

        if (worker is not null
            && (worker.ActiveRun is not null
                || !worker.Lifecycle.AllowsDirect()))
        {
            throw new InvalidOperationException(
                $"GroupChat '{Id}' cannot run a direct chat turn while a supervised Attempt is active.");
        }

        var snapshot = OrchestrationParticipants.Snapshot(Id, Participants);
        var shape = DirectOrchestrationShape.CreateGroupChat(GetType(), snapshot);
        var agent = shape.CreateAgent(GrainFactory, TaskScheduler.Current);
        return await _directSession.RunAsync(
            agent,
            shape.Definition,
            messages,
            CancellationToken.None);
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

        if (result.TerminalMessages is null)
        {
            StageWorkerState(state with
            {
                Checkpoint = result.OutputCheckpoint,
                ActiveRun = null,
                Lifecycle = SupervisedAttemptLifecycle.AwaitingContinuation,
            });
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
        StageWorkerState(state with
        {
            Checkpoint = result.OutputCheckpoint,
            ActiveRun = null,
            Lifecycle = SupervisedAttemptLifecycle.Succeeded,
        });
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
        var state = RequireCurrentDefinition(LoadWorkerState()
            ?? throw new InvalidOperationException(
                $"GroupChat '{Id}' has no supervised Attempt state."));

        if (!MatchesActive(state, run))
        {
            throw new InvalidOperationException(
                $"Workflow run '{run.RunId}' does not match GroupChat '{Id}'s active run fence.");
        }

        return state;
    }

    private static bool MatchesActive(AIWorkerState state, WorkflowRun run)
        => state.Lifecycle == SupervisedAttemptLifecycle.Running
            && state.ActiveRun is { } active
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

        if (current is null)
        {
            return;
        }

        RequireCurrentDefinition(current);

        if (!MatchesActive(current, run))
        {
            return;
        }

        var command = new WorkflowRunCommand(
            run,
            current.Definition,
            ChatMessageCopies.Clone(current.ReplayInput, _messages));

        await Runner(run).ExecuteAsync(command);
    }

    private IWorkflowRunner Runner(WorkflowRun run)
        => GrainFactory.GetGrain<IWorkflowRunner>(
            IdSpan.Create(WorkflowRunnerIdentity.GrainKey(run)));

    private OrchestrationDefinition CurrentSupervisedDefinition()
        => DirectOrchestrationShape
            .CreateGroupChat(
                GetType(),
                OrchestrationParticipants.Snapshot(Id, Participants))
            .Definition;

    private AIWorkerState RequireCurrentDefinition(AIWorkerState state)
    {
        OrchestrationDefinition.RequireMatch(
            state.Definition,
            CurrentSupervisedDefinition());

        return state;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The durable cancellation fence must remain authoritative when cooperative runner signaling fails.")]
    private async Task TryCancelRunnerAsync(WorkflowRun run)
    {
        try
        {
            await Runner(run).CancelAsync(run.RunId);
        }
        catch (Exception)
        {
        }
    }

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

}
