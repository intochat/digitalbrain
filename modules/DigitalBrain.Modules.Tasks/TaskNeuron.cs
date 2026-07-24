using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Tasks;

[GrainType("task")]
internal sealed class TaskNeuron :
    Neuron,
    ITask,
    IHandle<AttemptAccepted>,
    IHandle<AttemptAdvanced>,
    IHandle<AttemptProgressed>,
    IHandle<AttemptWaiting>,
    IHandle<AttemptSucceeded>,
    IHandle<AttemptFailed>,
    IHandle<AttemptCancelled>,
    IHandle<AttemptOutcomeUncertain>,
    IRemindable
{
    private const string StateName = "tasks.task";
    private const string RetryReminderName = "tasks.retry";
    private const string DispatchReminderName = "tasks.dispatch";
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<TaskData> _states;
    private IGrainTimer? _continuation;

    public TaskNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<TaskData>>();
    }

    public async Task<TaskSnapshot> Start(StartTask command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);

        if (_state.Value is { Length: > 0 })
        {
            var existing = Load();

            if (existing.Receipts.TryGetValue(command.CommandId, out var received))
            {
                return received;
            }

            throw new InvalidOperationException($"Task '{Id}' has already been started.");
        }

        Validate(command);

        if (command.Worker.Owner != Id.Owner)
        {
            throw new InvalidOperationException(
                $"Worker '{command.Worker}' does not belong to Task '{Id}'s owner.");
        }

        await ValidatePredecessorAsync(command.RetryOf);

        var attempt = new AttemptId(Guid.NewGuid());
        var data = new TaskData(
            command.Goal,
            command.Worker,
            command.Policy,
            TaskState.Pending,
            revision: 0,
            activeAttempt: attempt,
            blocker: null,
            result: null,
            failure: null,
            evidence: [],
            command.RetryOf,
            attemptCount: 1,
            receipts: new Dictionary<CommandId, TaskSnapshot>(),
            pendingDispatch: null);
        data.PendingDispatch = new AcceptWorkerDispatch(Request(data));
        var snapshot = Snapshot(data);
        data.Receipts.Add(command.CommandId, snapshot);

        await RegisterDispatchReminderAsync();
        await SaveAsync(data);
        await TryDispatchPendingAsync();

        return snapshot;
    }

    public async Task<TaskSnapshot> Cancel(CancelTask command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);

        var data = Load();

        if (data.Receipts.TryGetValue(command.CommandId, out var received))
        {
            return received;
        }

        if (command.ExpectedRevision != data.Revision)
        {
            throw new InvalidOperationException(
                $"Task '{Id}' is at revision {data.Revision}, not expected revision {command.ExpectedRevision}.");
        }

        if (IsTerminal(data.State))
        {
            var terminal = Snapshot(data);
            data.Receipts.Add(command.CommandId, terminal);
            await SaveAsync(data);
            return terminal;
        }

        if (data.ActiveAttempt is null)
        {
            data.State = TaskState.Cancelled;
            data.Blocker = null;
            data.PendingDispatch = null;

            var cancelled = Snapshot(data);
            data.Receipts.Add(command.CommandId, cancelled);
            await SaveAsync(data);
            await UnregisterReminderAsync(RetryReminderName);
            await UnregisterReminderAsync(DispatchReminderName);
            return cancelled;
        }

        data.State = TaskState.Cancelling;
        data.Blocker = null;
        data.PendingDispatch = new CancelWorkerDispatch(Cursor(data));

        var snapshot = Snapshot(data);
        data.Receipts.Add(command.CommandId, snapshot);

        await RegisterDispatchReminderAsync();
        await SaveAsync(data);
        await TryDispatchPendingAsync();

        return snapshot;
    }

    public Task<TaskSnapshot> Read() => Task.FromResult(Snapshot(Load()));

    public Task HandleAsync(AttemptAccepted fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || data.State != TaskState.Pending)
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Running;
        AcknowledgePendingDispatch(data, fact);

        Stage(data);
        return Task.CompletedTask;
    }

    public Task HandleAsync(AttemptWaiting fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Blocker is null)
        {
            return Task.CompletedTask;
        }

        var data = Load();

        if (!Matches(data, fact)
            || data.State is not (TaskState.Pending or TaskState.Running or TaskState.Waiting))
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Waiting;
        data.Blocker = fact.Blocker;
        AcknowledgePendingDispatch(data, fact);

        Stage(data);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(AttemptAdvanced fact, CancellationToken cancellationToken)
        => await AdvanceAsync(fact);

    public async Task HandleAsync(AttemptProgressed fact, CancellationToken cancellationToken)
        => await AdvanceAsync(fact);

    private async Task AdvanceAsync(AttemptFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact)
            || data.State is not (TaskState.Running or TaskState.Waiting))
        {
            return;
        }

        data.Revision++;
        data.State = TaskState.Running;
        data.Blocker = null;
        data.PendingDispatch = new ContinueWorkerDispatch(Cursor(data));

        await RegisterDispatchReminderAsync();
        ScheduleContinuation();
        Stage(data);
    }

    public Task HandleAsync(AttemptSucceeded fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Result is null || fact.Evidence is null)
        {
            return Task.CompletedTask;
        }

        var data = Load();

        if (!Matches(data, fact)
            || data.State is TaskState.Succeeded or TaskState.Failed or TaskState.Cancelled)
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Succeeded;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = fact.Result;
        data.Failure = null;
        data.Evidence = [.. fact.Evidence];
        data.PendingDispatch = null;

        Stage(data);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(AttemptFailed fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Failure is null)
        {
            return;
        }

        var data = Load();

        if (!Matches(data, fact) || IsTerminal(data.State))
        {
            return;
        }

        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = null;
        data.Failure = fact.Failure;
        data.Evidence = [];
        data.PendingDispatch = null;

        if (fact.Retryable
            && data.State != TaskState.Cancelling
            && data.AttemptCount < data.Policy.MaximumAttempts
            && (data.Policy.Deadline is null || data.Policy.Deadline > DateTimeOffset.UtcNow))
        {
            data.State = TaskState.Waiting;
            data.Blocker = new RetryScheduled(new BlockerId(Guid.NewGuid()));
            await this.RegisterOrUpdateReminder(
                RetryReminderName,
                data.Policy.RetryDelay,
                ReminderPeriod);
            Stage(data);
            return;
        }

        data.State = TaskState.Failed;

        Stage(data);
    }

    public Task HandleAsync(AttemptCancelled fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || data.State != TaskState.Cancelling)
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Cancelled;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.PendingDispatch = null;

        Stage(data);
        return Task.CompletedTask;
    }

    public Task HandleAsync(AttemptOutcomeUncertain fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (fact.Blocker.Value == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        var data = Load();

        if (!Matches(data, fact) || IsTerminal(data.State))
        {
            return Task.CompletedTask;
        }

        data.State = TaskState.Waiting;
        data.Blocker = new OutcomeUncertain(fact.Blocker);
        data.PendingDispatch = null;

        Stage(data);
        return Task.CompletedTask;
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (string.Equals(reminderName, DispatchReminderName, StringComparison.Ordinal))
        {
            await TryDispatchPendingAsync();
            return;
        }

        if (!string.Equals(reminderName, RetryReminderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Task neuron '{Id}' does not own reminder '{reminderName}'.");
        }

        var data = LoadIfStarted();

        if (data is null)
        {
            await UnregisterReminderAsync(RetryReminderName);
            return;
        }

        if (data.State != TaskState.Waiting
            || data.Blocker is not RetryScheduled
            || data.AttemptCount >= data.Policy.MaximumAttempts
            || (data.Policy.Deadline is not null && data.Policy.Deadline <= DateTimeOffset.UtcNow))
        {
            await UnregisterReminderAsync(RetryReminderName);
            return;
        }

        data.Revision++;
        data.State = TaskState.Pending;
        data.ActiveAttempt = new AttemptId(Guid.NewGuid());
        data.Blocker = null;
        data.AttemptCount++;
        data.PendingDispatch = new AcceptWorkerDispatch(Request(data));

        await RegisterDispatchReminderAsync();
        await SaveAsync(data);
        await UnregisterReminderAsync(RetryReminderName);
        await TryDispatchPendingAsync();
    }

    Task INeuron.Deliver(SynapseDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return delivery.Synapse is AttemptFact fact && delivery.Caller != fact.Worker
            ? Task.CompletedTask
            : base.Deliver(delivery);
    }

    private TaskData Load()
    {
        if (LoadIfStarted() is not { } data)
        {
            throw new InvalidOperationException($"Task '{Id}' has not been started.");
        }

        return data;
    }

    private TaskData? LoadIfStarted()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(TaskData data)
        => _state.Value = _states.SerializeToArray(data);

    private async Task SaveAsync(TaskData data)
    {
        Stage(data);
        await WriteStateAsync();
    }

    private Task<Orleans.Runtime.IGrainReminder> RegisterDispatchReminderAsync()
        => this.RegisterOrUpdateReminder(
            DispatchReminderName,
            TimeSpan.FromSeconds(1),
            ReminderPeriod);

    private async Task UnregisterReminderAsync(string reminderName)
    {
        if (await this.GetReminder(reminderName) is { } reminder)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A durable pending dispatch remains registered for reminder-driven redelivery after any Worker failure.")]
    private async Task TryDispatchPendingAsync()
    {
        var data = LoadIfStarted();

        if (data is null)
        {
            await UnregisterReminderAsync(DispatchReminderName);
            return;
        }

        var pending = data.PendingDispatch;

        if (pending is null)
        {
            await UnregisterReminderAsync(DispatchReminderName);
            return;
        }

        if (pending is not (AcceptWorkerDispatch or ContinueWorkerDispatch or CancelWorkerDispatch))
        {
            throw new InvalidOperationException(
                $"Task '{Id}' has an unsupported pending Worker dispatch '{pending.GetType().Name}'.");
        }

        try
        {
            var worker = Worker(data);

            switch (pending)
            {
                case AcceptWorkerDispatch accept:
                    await worker.Accept(accept.Request);
                    break;

                case ContinueWorkerDispatch continuation:
                    await worker.Continue(continuation.Cursor);
                    break;

                case CancelWorkerDispatch cancellation:
                    await worker.Cancel(cancellation.Cursor);
                    break;
            }
        }
        catch (Exception)
        {
            await RegisterDispatchReminderAsync();
            return;
        }

        var current = Load();

        if (current.PendingDispatch != pending)
        {
            return;
        }

        current.PendingDispatch = null;
        await SaveAsync(current);
        await UnregisterReminderAsync(DispatchReminderName);
    }

    private IWorker Worker(TaskData data)
        => GrainFactory.GetGrain<IWorker>(data.Worker.ToGrainId());

    private AttemptCursor Cursor(TaskData data) => new(
        Id,
        data.Worker,
        data.ActiveAttempt
            ?? throw new InvalidOperationException($"Task '{Id}' has no active Attempt."),
        data.Revision);

    private AttemptRequest Request(TaskData data) => new(
        Id,
        data.Worker,
        data.ActiveAttempt
            ?? throw new InvalidOperationException($"Task '{Id}' has no active Attempt."),
        data.Revision,
        data.Goal);

    private void ScheduleContinuation()
    {
        _continuation?.Dispose();
        _continuation = RegisterGrainTimer(
            async () =>
            {
                _continuation?.Dispose();
                _continuation = null;

                await TryDispatchPendingAsync();
            },
            new GrainTimerCreationOptions(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
    }

    private bool Matches(TaskData data, AttemptFact fact)
    {
        if (fact.Task != Id
            || fact.Worker != data.Worker
            || fact.Attempt != data.ActiveAttempt)
        {
            return false;
        }

        return fact.Revision == data.Revision;
    }

    private static void AcknowledgePendingDispatch(TaskData data, AttemptFact fact)
    {
        var matches = data.PendingDispatch switch
        {
            AcceptWorkerDispatch { Request: var request } =>
                request.Task == fact.Task
                && request.Worker == fact.Worker
                && request.Attempt == fact.Attempt
                && request.Revision == fact.Revision,
            ContinueWorkerDispatch { Cursor: var cursor } =>
                cursor.Task == fact.Task
                && cursor.Worker == fact.Worker
                && cursor.Attempt == fact.Attempt
                && cursor.Revision == fact.Revision,
            _ => false
        };

        if (matches)
        {
            data.PendingDispatch = null;
        }
    }

    private static TaskSnapshot Snapshot(TaskData data) => new(
        data.Goal,
        data.Worker,
        data.Policy,
        data.State,
        data.Revision,
        data.ActiveAttempt,
        data.Blocker,
        data.Result,
        data.Failure,
        [.. data.Evidence],
        data.RetryOf);

    private static void Validate(StartTask command)
    {
        ArgumentNullException.ThrowIfNull(command.Goal);
        ArgumentNullException.ThrowIfNull(command.Policy);

        if (command.Policy.MaximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "A task policy must allow at least one attempt.");
        }

        if (command.Policy.RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "A task retry delay cannot be negative.");
        }

        if (command.Worker == default)
        {
            throw new ArgumentException("A task worker is required.", nameof(command));
        }

    }

    private static void Validate(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("A command id is required.", nameof(commandId));
        }
    }

    private async Task ValidatePredecessorAsync(NeuronId? predecessor)
    {
        if (predecessor is null)
        {
            return;
        }

        if (predecessor == Id || predecessor.Value.Owner != Id.Owner)
        {
            throw new InvalidOperationException(
                $"Task '{predecessor}' cannot be the predecessor of Task '{Id}'.");
        }

        var snapshot = await GrainFactory
            .GetGrain<ITask>(predecessor.Value.ToGrainId())
            .Read();

        if (!IsTerminal(snapshot.State))
        {
            throw new InvalidOperationException(
                $"Task '{predecessor}' is not terminal, so Task '{Id}' cannot retry it.");
        }
    }

    private static bool IsTerminal(TaskState state)
        => state is TaskState.Succeeded or TaskState.Failed or TaskState.Cancelled;
}

[GenerateSerializer]
[Alias("tasks.persisted-state")]
internal sealed class TaskData(
    Goal goal,
    NeuronId worker,
    TaskPolicy policy,
    TaskState state,
    long revision,
    AttemptId? activeAttempt,
    TaskBlocker? blocker,
    Result? result,
    Failure? failure,
    FactReference[] evidence,
    NeuronId? retryOf,
    int attemptCount,
    Dictionary<CommandId, TaskSnapshot> receipts,
    PendingWorkerDispatch? pendingDispatch)
{
    [Id(0)]
    public Goal Goal { get; set; } = goal;

    [Id(1)]
    public NeuronId Worker { get; set; } = worker;

    [Id(2)]
    public TaskPolicy Policy { get; set; } = policy;

    [Id(3)]
    public TaskState State { get; set; } = state;

    [Id(4)]
    public long Revision { get; set; } = revision;

    [Id(5)]
    public AttemptId? ActiveAttempt { get; set; } = activeAttempt;

    [Id(6)]
    public TaskBlocker? Blocker { get; set; } = blocker;

    [Id(7)]
    public Result? Result { get; set; } = result;

    [Id(8)]
    public Failure? Failure { get; set; } = failure;

    [Id(9)]
    public FactReference[] Evidence { get; set; } = evidence;

    [Id(10)]
    public NeuronId? RetryOf { get; set; } = retryOf;

    [Id(11)]
    public int AttemptCount { get; set; } = attemptCount;

    [Id(12)]
    public Dictionary<CommandId, TaskSnapshot> Receipts { get; set; } = receipts;

    [Id(13)]
    public PendingWorkerDispatch? PendingDispatch { get; set; } = pendingDispatch;
}

[GenerateSerializer]
[Alias("tasks.pending-worker-dispatch")]
internal abstract record PendingWorkerDispatch;

[GenerateSerializer]
[Alias("tasks.pending-worker-accept")]
internal sealed record AcceptWorkerDispatch(
    [property: Id(0)] AttemptRequest Request) : PendingWorkerDispatch;

[GenerateSerializer]
[Alias("tasks.pending-worker-continue")]
internal sealed record ContinueWorkerDispatch(
    [property: Id(0)] AttemptCursor Cursor) : PendingWorkerDispatch;

[GenerateSerializer]
[Alias("tasks.pending-worker-cancel")]
internal sealed record CancelWorkerDispatch(
    [property: Id(0)] AttemptCursor Cursor) : PendingWorkerDispatch;
