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
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<TaskData> _states;
    private IGrainTimer? _continuation;

    public TaskNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<TaskData>>();
    }

    public async Task<TaskSnapshot> StartAsync(StartTask command)
    {
        ArgumentNullException.ThrowIfNull(command);

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
            receipts: new Dictionary<CommandId, TaskSnapshot>());
        var snapshot = Snapshot(data);
        data.Receipts.Add(command.CommandId, snapshot);

        await SaveAsync(data);
        await Worker(data).AcceptAsync(new(
            Id,
            data.Worker,
            attempt,
            data.Revision,
            data.Goal));

        return snapshot;
    }

    public async Task<TaskSnapshot> CancelAsync(CancelTask command)
    {
        ArgumentNullException.ThrowIfNull(command);

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
            var reminder = await this.GetReminder(RetryReminderName);

            if (reminder is not null)
            {
                await this.UnregisterReminder(reminder);
            }

            data.State = TaskState.Cancelled;
            data.Blocker = null;

            var cancelled = Snapshot(data);
            data.Receipts.Add(command.CommandId, cancelled);
            await SaveAsync(data);
            return cancelled;
        }

        data.State = TaskState.Cancelling;
        data.Blocker = null;

        var snapshot = Snapshot(data);
        data.Receipts.Add(command.CommandId, snapshot);

        await SaveAsync(data);
        await Worker(data).CancelAsync(Cursor(data));

        return snapshot;
    }

    public Task<TaskSnapshot> ReadAsync() => Task.FromResult(Snapshot(Load()));

    public async Task HandleAsync(AttemptAccepted fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || data.State != TaskState.Pending)
        {
            return;
        }

        data.State = TaskState.Running;

        await SaveAsync(data);
    }

    public async Task HandleAsync(AttemptWaiting fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact)
            || data.State is not (TaskState.Pending or TaskState.Running or TaskState.Waiting))
        {
            return;
        }

        data.State = TaskState.Waiting;
        data.Blocker = fact.Blocker;

        await SaveAsync(data);
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

        await SaveAsync(data);
        ScheduleContinuation(Cursor(data));
    }

    public async Task HandleAsync(AttemptSucceeded fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact)
            || data.State is TaskState.Succeeded or TaskState.Failed or TaskState.Cancelled)
        {
            return;
        }

        data.State = TaskState.Succeeded;
        data.ActiveAttempt = null;
        data.Blocker = null;
        data.Result = fact.Result;
        data.Failure = null;
        data.Evidence = [.. fact.Evidence];

        await SaveAsync(data);
    }

    public async Task HandleAsync(AttemptFailed fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

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

        if (fact.Retryable
            && data.State != TaskState.Cancelling
            && data.AttemptCount < data.Policy.MaximumAttempts
            && (data.Policy.Deadline is null || data.Policy.Deadline > DateTimeOffset.UtcNow))
        {
            data.State = TaskState.Waiting;
            data.Blocker = new RetryScheduled(new BlockerId(Guid.NewGuid()));
            await SaveAsync(data);
            await this.RegisterOrUpdateReminder(
                RetryReminderName,
                data.Policy.RetryDelay,
                ReminderPeriod);
            return;
        }

        data.State = TaskState.Failed;

        await SaveAsync(data);
    }

    public async Task HandleAsync(AttemptCancelled fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || data.State != TaskState.Cancelling)
        {
            return;
        }

        data.State = TaskState.Cancelled;
        data.ActiveAttempt = null;
        data.Blocker = null;

        await SaveAsync(data);
    }

    public async Task HandleAsync(AttemptOutcomeUncertain fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var data = Load();

        if (!Matches(data, fact) || IsTerminal(data.State))
        {
            return;
        }

        data.State = TaskState.Waiting;
        data.Blocker = new OutcomeUncertain(fact.Blocker);

        await SaveAsync(data);
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, RetryReminderName, StringComparison.Ordinal))
        {
            await base.ReceiveReminder(reminderName, status);
            return;
        }

        var reminder = await this.GetReminder(RetryReminderName);

        if (reminder is not null)
        {
            await this.UnregisterReminder(reminder);
        }

        var data = Load();

        if (data.State != TaskState.Waiting
            || data.Blocker is not RetryScheduled
            || data.AttemptCount >= data.Policy.MaximumAttempts
            || (data.Policy.Deadline is not null && data.Policy.Deadline <= DateTimeOffset.UtcNow))
        {
            return;
        }

        data.Revision++;
        data.State = TaskState.Pending;
        data.ActiveAttempt = new AttemptId(Guid.NewGuid());
        data.Blocker = null;
        data.AttemptCount++;

        await SaveAsync(data);
        await Worker(data).AcceptAsync(Request(data));
    }

    Task INeuron.DeliverAsync(SynapseDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return delivery.Synapse is AttemptFact fact && delivery.Caller != fact.Worker
            ? Task.CompletedTask
            : base.DeliverAsync(delivery);
    }

    private TaskData Load()
    {
        if (_state.Value is not { Length: > 0 } serialized)
        {
            throw new InvalidOperationException($"Task '{Id}' has not been started.");
        }

        return _states.Deserialize(serialized);
    }

    private async Task SaveAsync(TaskData data)
    {
        _state.Value = _states.SerializeToArray(data);
        await WriteStateAsync();
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

    private void ScheduleContinuation(AttemptCursor cursor)
    {
        _continuation?.Dispose();
        _continuation = RegisterGrainTimer(
            async () =>
            {
                _continuation?.Dispose();
                _continuation = null;

                var current = Load();

                if (current.ActiveAttempt != cursor.Attempt
                    || current.Revision != cursor.Revision
                    || current.State != TaskState.Running)
                {
                    return;
                }

                await Worker(current).ContinueAsync(cursor);
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

        if (fact.Revision > data.Revision)
        {
            throw new InvalidOperationException(
                $"Attempt fact revision {fact.Revision} is ahead of Task '{Id}' revision {data.Revision}.");
        }

        return fact.Revision == data.Revision;
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
            .ReadAsync();

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
    Dictionary<CommandId, TaskSnapshot> receipts)
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
}
