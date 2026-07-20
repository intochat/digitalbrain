using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using System.Collections.Concurrent;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class TaskLifecycleContracts
{
    [Fact(DisplayName = "a durable Task completes from typed facts emitted by its scripted Worker")]
    public async Task ATaskCompletesFromItsWorkerFacts()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-tracer");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());
        var start = await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal("prove task lifecycle"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));

        Assert.Equal(TaskState.Pending, start.State);
        Assert.Equal(0, start.Revision);
        Assert.NotNull(start.ActiveAttempt);

        var completed = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);

        Assert.Equal(new TracerResult("done"), completed.Result);
        Assert.Null(completed.ActiveAttempt);
    }

    [Fact(DisplayName = "a Worker blocker puts the Task into the typed Waiting state")]
    public async Task AWorkerBlockerPutsTheTaskIntoWaiting()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-waiting");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());
        var blockerId = new BlockerId(Guid.NewGuid());

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal($"wait:{blockerId.Value:D}"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));

        var waiting = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Waiting);

        Assert.Equal(new InputRequired(blockerId), waiting.Blocker);
        Assert.NotNull(waiting.ActiveAttempt);
    }

    [Fact(DisplayName = "an advanced Attempt continues only at the next revision")]
    public async Task AnAdvancedAttemptContinuesAtTheNextRevision()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-revision");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal("advance"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));

        var completed = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);

        Assert.Equal(1, completed.Revision);
        Assert.Equal(new TracerResult("continued:1"), completed.Result);
    }

    [Fact(DisplayName = "Task start is command-idempotent, singular, and durable across restart")]
    public async Task TaskStartIsIdempotentSingularAndDurable()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-idempotency");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());
        var command = new StartTask(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(task.ReadAsync);

        var first = await task.StartAsync(command);
        var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
        var repeated = await task.StartAsync(command);

        AssertEquivalent(first, repeated);
        Assert.Equal(first.ActiveAttempt, running.ActiveAttempt);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => task.StartAsync(command with
        {
            CommandId = CommandId.New(),
        }));

        await SimulationCluster.RestartHostOfAsync(taskId);

        var restored = await task.ReadAsync();

        Assert.Equal(running.ActiveAttempt, restored.ActiveAttempt);
        Assert.Equal(TaskState.Running, restored.State);
    }

    [Fact(DisplayName = "cancellation reports the outcome that wins and terminal success is immutable")]
    public async Task CancellationReportsTheOutcomeThatWins()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-cancellation-race");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var evidenceId = new SynapseId(Guid.NewGuid());
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal($"cancel-success:{evidenceId.Value:D}"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => task.CancelAsync(new(
            CommandId.New(),
            running.Revision + 1)));

        var cancelling = await task.CancelAsync(new(CommandId.New(), running.Revision));
        var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);

        Assert.Equal(TaskState.Cancelling, cancelling.State);
        Assert.Equal(new TracerResult("won cancellation race"), succeeded.Result);
        Assert.Equal([new FactReference(workerId, evidenceId)], succeeded.Evidence);

        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        AssertEquivalent(succeeded, await task.ReadAsync());
    }

    [Fact(DisplayName = "a retryable failure creates a new Attempt through the private durable reminder")]
    public async Task RetryableFailureCreatesANewAttempt()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-retry");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());
        var started = await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal("retry"),
            workerId,
            new TaskPolicy(2, TimeSpan.FromMilliseconds(250), null)));

        var waiting = await ReadUntilAsync(
            task,
            snapshot => snapshot.State == TaskState.Waiting && snapshot.Blocker is RetryScheduled);
        var reminderId = NeuronId.For<ReminderProbe>(owner, "reminder");
        var reminder = SimulationCluster.Grains.GetGrain<IReminderProbe>(reminderId.ToGrainId());

        await reminder.TickAsync(taskId, "tasks.retry");

        var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);

        Assert.IsType<TracerFailure>(waiting.Failure);
        Assert.Equal(1, succeeded.Revision);
        var result = Assert.IsType<TracerResult>(succeeded.Result);
        Assert.StartsWith("retried:", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(started.ActiveAttempt!.Value.Value.ToString("D"), result.Value, StringComparison.Ordinal);

        await reminder.TickAsync(taskId, "db.outbox");
    }

    [Fact(DisplayName = "an uncertain outcome waits without automatic retry")]
    public async Task UncertainOutcomeWaitsWithoutRetry()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-uncertain");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var blockerId = new BlockerId(Guid.NewGuid());
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal($"uncertain:{blockerId.Value:D}"),
            workerId,
            new TaskPolicy(3, TimeSpan.Zero, null)));

        var waiting = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Waiting);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        Assert.Equal(new OutcomeUncertain(blockerId), waiting.Blocker);
        AssertEquivalent(waiting, await task.ReadAsync());
    }

    [Fact(DisplayName = "an Attempt fact is accepted only from the declared Worker")]
    public async Task AttemptFactIsAcceptedOnlyFromTheDeclaredWorker()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-caller-fence");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
        var injectorId = NeuronId.For<FactInjector>(owner, "injector");
        var injector = SimulationCluster.Grains.GetGrain<IFactInjector>(injectorId.ToGrainId());

        await injector.SendAsync(new AttemptSucceeded(
            taskId,
            workerId,
            running.ActiveAttempt!.Value,
            running.Revision,
            new TracerResult("forged"),
            []));
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        AssertEquivalent(running, await task.ReadAsync());
    }

    [Fact(DisplayName = "retry is a new successor Task and only a terminal Task may be its predecessor")]
    public async Task RetryIsASuccessorOfATerminalTask()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-successor");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var activeId = NeuronId.For<ITask>(owner, "active");
        var active = SimulationCluster.Grains.GetGrain<ITask>(activeId.ToGrainId());

        await active.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        await ReadUntilAsync(active, snapshot => snapshot.State == TaskState.Running);

        var refusedId = NeuronId.For<ITask>(owner, "refused-successor");
        var refused = SimulationCluster.Grains.GetGrain<ITask>(refusedId.ToGrainId());

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => refused.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null),
            activeId)));

        var terminalId = NeuronId.For<ITask>(owner, "terminal");
        var terminal = SimulationCluster.Grains.GetGrain<ITask>(terminalId.ToGrainId());
        await terminal.StartAsync(new(
            CommandId.New(),
            new TracerGoal("complete"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        await ReadUntilAsync(terminal, snapshot => snapshot.State == TaskState.Succeeded);

        var successorId = NeuronId.For<ITask>(owner, "successor");
        var successor = SimulationCluster.Grains.GetGrain<ITask>(successorId.ToGrainId());
        var started = await successor.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null),
            terminalId));

        Assert.Equal(terminalId, started.RetryOf);
        Assert.NotEqual(terminalId, successorId);
    }

    private static async Task<TaskSnapshot> ReadUntilAsync(
        ITask task,
        Func<TaskSnapshot, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        while (true)
        {
            var snapshot = await task.ReadAsync();

            if (condition(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static void AssertEquivalent(TaskSnapshot expected, TaskSnapshot actual)
    {
        Assert.Equal(expected.Goal, actual.Goal);
        Assert.Equal(expected.Worker, actual.Worker);
        Assert.Equal(expected.Policy, actual.Policy);
        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.ActiveAttempt, actual.ActiveAttempt);
        Assert.Equal(expected.Blocker, actual.Blocker);
        Assert.Equal(expected.Result, actual.Result);
        Assert.Equal(expected.Failure, actual.Failure);
        Assert.Equal(expected.Evidence, actual.Evidence);
        Assert.Equal(expected.RetryOf, actual.RetryOf);
    }
}

[GenerateSerializer]
[Alias("db.test.tracer-goal")]
internal sealed record TracerGoal([property: Id(0)] string Description) : Goal;

[GenerateSerializer]
[Alias("db.test.tracer-result")]
internal sealed record TracerResult([property: Id(0)] string Value) : Result;

[GenerateSerializer]
[Alias("db.test.tracer-failure")]
internal sealed record TracerFailure([property: Id(0)] string Value) : Failure;

internal sealed class ScriptedWorker :
    Neuron,
    IWorker,
    IEmit<AttemptAccepted>,
    IEmit<AttemptAdvanced>,
    IEmit<AttemptSucceeded>,
    IEmit<AttemptWaiting>,
    IEmit<AttemptFailed>,
    IEmit<AttemptCancelled>,
    IEmit<AttemptOutcomeUncertain>
{
    private static readonly ConcurrentDictionary<NeuronId, string> Scripts = new();
    private static readonly ConcurrentDictionary<NeuronId, int> Acceptances = new();

    public async Task AcceptAsync(AttemptRequest request)
    {
        var description = ((TracerGoal)request.Goal).Description;
        Scripts[request.Task] = description;

        if (description == "hold" || description.StartsWith("cancel-", StringComparison.Ordinal))
        {
            await SendAsync(
                request.Task,
                new AttemptAccepted(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision));
            return;
        }

        if (description == "retry")
        {
            var acceptance = Acceptances.AddOrUpdate(request.Task, 1, static (_, count) => count + 1);

            if (acceptance == 1)
            {
                await SendAsync(
                    request.Task,
                    new AttemptFailed(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        new TracerFailure("transient"),
                        Retryable: true));
            }
            else
            {
                await SendAsync(
                    request.Task,
                    new AttemptSucceeded(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        new TracerResult($"retried:{request.Attempt.Value:D}"),
                        []));
            }

            return;
        }

        if (description.StartsWith("uncertain:", StringComparison.Ordinal))
        {
            await SendAsync(
                request.Task,
                new AttemptOutcomeUncertain(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new BlockerId(Guid.Parse(description.AsSpan(10)))));
            return;
        }

        if (description.StartsWith("wait:", StringComparison.Ordinal))
        {
            await SendAsync(
                request.Task,
                new AttemptWaiting(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new InputRequired(new BlockerId(Guid.Parse(description.AsSpan(5))))));
            return;
        }

        if (request.Goal is TracerGoal { Description: "advance" })
        {
            await SendAsync(
                request.Task,
                new AttemptAccepted(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision));
            await SendAsync(
                request.Task,
                new AttemptAdvanced(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision));
            return;
        }

        await SendAsync(
            request.Task,
            new AttemptSucceeded(
                request.Task,
                request.Worker,
                request.Attempt,
                request.Revision,
                new TracerResult("done"),
                []));
    }

    public Task ContinueAsync(AttemptCursor cursor)
        => ContinueCoreAsync(cursor);

    private async Task ContinueCoreAsync(AttemptCursor cursor)
    {
        await SendAsync(
            cursor.Task,
            new AttemptSucceeded(
                cursor.Task,
                cursor.Worker,
                cursor.Attempt,
                cursor.Revision - 1,
                new TracerResult("stale"),
                []));
        await SendAsync(
            cursor.Task,
            new AttemptSucceeded(
                cursor.Task,
                cursor.Worker,
                cursor.Attempt,
                cursor.Revision,
                new TracerResult($"continued:{cursor.Revision}"),
                []));
    }

    public async Task CancelAsync(AttemptCursor cursor)
    {
        var script = Scripts[cursor.Task];

        if (script.StartsWith("cancel-success:", StringComparison.Ordinal))
        {
            var evidence = new SynapseId(Guid.Parse(script.AsSpan(15)));

            await SendAsync(
                cursor.Task,
                new AttemptSucceeded(
                    cursor.Task,
                    cursor.Worker,
                    cursor.Attempt,
                    cursor.Revision,
                    new TracerResult("won cancellation race"),
                    [new FactReference(cursor.Worker, evidence)]));
            await SendAsync(
                cursor.Task,
                new AttemptFailed(
                    cursor.Task,
                    cursor.Worker,
                    cursor.Attempt,
                    cursor.Revision,
                    new TracerFailure("late"),
                    Retryable: false));
            await SendAsync(
                cursor.Task,
                new AttemptCancelled(
                    cursor.Task,
                    cursor.Worker,
                    cursor.Attempt,
                    cursor.Revision));
            return;
        }

        await SendAsync(
            cursor.Task,
            new AttemptCancelled(
                cursor.Task,
                cursor.Worker,
                cursor.Attempt,
                cursor.Revision));
    }
}

[Alias("db.test.fact-injector")]
[ClientEntryPoint]
internal interface IFactInjector : INeuron
{
    [Alias("Send")]
    Task SendAsync(AttemptFact fact);
}

internal sealed class FactInjector : Neuron, IFactInjector
{
    public Task SendAsync(AttemptFact fact) => SendAsync(fact.Task, fact);
}

[Alias("db.test.reminder-probe")]
[ClientEntryPoint]
internal interface IReminderProbe : INeuron
{
    [Alias("Tick")]
    Task TickAsync(NeuronId task, string reminderName);
}

internal sealed class ReminderProbe : Neuron, IReminderProbe
{
    public Task TickAsync(NeuronId task, string reminderName)
        => GrainFactory
            .GetGrain<IRemindable>(task.ToGrainId())
            .ReceiveReminder(reminderName, default);
}
