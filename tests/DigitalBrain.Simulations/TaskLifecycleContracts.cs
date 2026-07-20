using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class TaskLifecycleContracts
{
    [Fact(DisplayName = "raw clients cannot invoke ITask without a same-owner neuron")]
    public async Task RawClientsCannotInvokeTask()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-raw-client-refused");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var task = SimulationCluster.Grains.GetGrain<ITask>(taskId.ToGrainId());

        var refusal = await Assert.ThrowsAsync<NeuronAuthorizationException>(task.ReadAsync);
        var remindable = SimulationCluster.Grains.GetGrain<IRemindable>(taskId.ToGrainId());
        _ = await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => remindable.ReceiveReminder("tasks.dispatch", default));

        Assert.Contains("is not a client entry point", refusal.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Task capabilities are causally journaled and owner-bound")]
    public async Task TaskCapabilitiesAreCausallyJournaledAndOwnerBound()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-capability-lineage");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = TaskFor(taskId);
        var command = new StartTask(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null));

        await task.StartAsync(command);

        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);
        var callerOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            "task-driver",
            "driver-task",
            afterSequence: 0);
        var taskIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            "task",
            "task",
            afterSequence: 0);
        var requested = Assert.Single(callerOutgoing.Delta, Is<CapabilityRequested>);
        var request = Assert.IsType<CapabilityRequested>(requested.Synapse);

        Assert.Equal(typeof(ITask).FullName, request.Contract);
        Assert.Equal(nameof(ITask.StartAsync), request.Method);
        Assert.Equal(taskId, request.Target);
        Assert.Contains(taskIncoming.Delta, delivery => delivery.SynapseId == requested.SynapseId);

        var foreignOwner = new OwnerId("task-capability-foreign");
        var foreignDriverId = NeuronId.For<TaskDriver>(foreignOwner, "driver");
        var foreignDriver = SimulationCluster.Grains.GetGrain<ITaskDriver>(foreignDriverId.ToGrainId());
        _ = await Assert.ThrowsAsync<NeuronAuthorizationException>(() => foreignDriver.ReadAsync(taskId));

        var foreignOutgoing = await Simulation.ReadJournalOfOwnerAsync(
            JournalKind.Outgoing,
            foreignOwner.Value,
            "task-driver",
            "driver",
            afterSequence: 0);
        Assert.Single(foreignOutgoing.Delta, Is<CapabilityRejected>);
    }

    [Fact(DisplayName = "default command ids are rejected without changing durable Task state")]
    public async Task DefaultCommandIdsAreRejectedWithoutChangingState()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-default-command-id");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = TaskFor(taskId);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => task.StartAsync(new(
            default,
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null))));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(task.ReadAsync);

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);

        _ = await Assert.ThrowsAsync<ArgumentException>(() => task.CancelAsync(new(default, running.Revision)));

        AssertEquivalent(running, await task.ReadAsync());
    }

    [Theory(DisplayName = "typed null Attempt payloads are rejected before Task state is staged")]
    [InlineData("invalid-blocker")]
    [InlineData("invalid-failure")]
    [InlineData("invalid-result")]
    [InlineData("invalid-evidence")]
    public async Task InvalidAttemptPayloadsDoNotMutateState(string script)
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId($"task-{script}");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = TaskFor(taskId);

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal(script),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);

        await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

        AssertEquivalent(running, await task.ReadAsync());
    }

    [Fact(DisplayName = "future-revision Attempt facts are durably ignored without retry storms")]
    public async Task FutureRevisionFactsAreDurablyIgnored()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-future-fact");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = TaskFor(taskId);

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
        var future = new AttemptSucceeded(
            taskId,
            workerId,
            running.ActiveAttempt!.Value,
            running.Revision + 1,
            new TracerResult("future"),
            []);
        var worker = SimulationCluster.Grains.GetGrain<IScriptedWorkerControl>(workerId.ToGrainId());

        await worker.SendFactAsync(future);
        await AwaitIncomingAsync(taskId, future);

        AssertEquivalent(running, await task.ReadAsync());
    }

    [Fact(DisplayName = "Attempt progression and its Task revision survive restart together")]
    public async Task AttemptProgressionCommitsWithItsIncomingFact()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-atomic-progress");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = TaskFor(taskId);

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal("progress-hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
        var progressed = new AttemptProgressed(
            taskId,
            workerId,
            running.ActiveAttempt!.Value,
            running.Revision);
        var worker = SimulationCluster.Grains.GetGrain<IScriptedWorkerControl>(workerId.ToGrainId());

        await worker.SendFactAsync(progressed);
        var advanced = await ReadUntilAsync(task, snapshot => snapshot.Revision == 1);
        await AwaitIncomingAsync(taskId, progressed);
        await SimulationCluster.RestartHostOfAsync(taskId);

        Assert.Equal(1, advanced.Revision);
        Assert.Equal(1, (await task.ReadAsync()).Revision);
    }

    [Theory(DisplayName = "public terminal and progressed Attempt facts drive their Task outcomes")]
    [InlineData("cancel", TaskState.Cancelled)]
    [InlineData("fail", TaskState.Failed)]
    [InlineData("progress", TaskState.Succeeded)]
    public async Task PublicAttemptFactsDriveTaskOutcomes(string script, TaskState expected)
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId($"task-public-{script}");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = TaskFor(taskId);

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal(script),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));

        if (script == "cancel")
        {
            var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await task.CancelAsync(new(CommandId.New(), running.Revision));
        }

        var terminal = await ReadUntilAsync(task, snapshot => snapshot.State == expected);

        if (script == "fail")
        {
            Assert.IsType<TracerFailure>(terminal.Failure);
        }

        if (script == "progress")
        {
            Assert.Equal(1, terminal.Revision);
        }
    }

    [Theory(DisplayName = "durable pending Worker dispatch recovers after a transient Worker failure")]
    [InlineData("recover-accept")]
    [InlineData("recover-continue")]
    [InlineData("recover-cancel")]
    public async Task PendingWorkerDispatchRecovers(string script)
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId($"task-{script}");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = TaskFor(taskId);

        await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal(script),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));

        var reminderId = NeuronId.For<ReminderProbe>(owner, "dispatch-reminder-probe");
        var reminder = SimulationCluster.Grains.GetGrain<IReminderProbe>(reminderId.ToGrainId());

        if (script == "recover-accept")
        {
            await WaitForReminderAsync(reminder, taskId, "tasks.dispatch");
            await SimulationCluster.RestartHostOfAsync(taskId);
        }

        if (script == "recover-cancel")
        {
            var running = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Running);
            await task.CancelAsync(new(CommandId.New(), running.Revision));
        }

        if (script != "recover-accept")
        {
            await WaitForReminderAsync(reminder, taskId, "tasks.dispatch");
        }

        var expected = script == "recover-cancel" ? TaskState.Cancelled : TaskState.Succeeded;
        var terminal = await ReadUntilAsync(task, snapshot => snapshot.State == expected);

        Assert.True(ScriptedWorker.DispatchCount(taskId) >= 2);
        Assert.Equal(expected, terminal.State);
    }

    [Fact(DisplayName = "a forwarded Kernel db.outbox reminder drains delivery after sender restart")]
    public async Task ForwardedKernelOutboxReminderDrainsAfterRestart()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-kernel-outbox-recovery");
        var relayId = NeuronId.For<OutboxForwardingRelay>(owner, "relay");
        var receiverId = NeuronId.For<OutboxRecoveryReceiver>(owner, "receiver");
        var simulation = new Simulation();
        simulation.OpenBrain(owner.Value);

        await simulation.SendAsync(
            nameof(OutboxRecoveryRequested),
            nameof(OutboxForwardingRelay),
            relayId.Name,
            new Dictionary<string, string>(StringComparer.Ordinal));
        await WaitForOutboxAttemptAsync(receiverId);

        var reminderId = NeuronId.For<ReminderProbe>(owner, "outbox-reminder-probe");
        var reminder = SimulationCluster.Grains.GetGrain<IReminderProbe>(reminderId.ToGrainId());
        await WaitForReminderAsync(reminder, relayId, "db.outbox");
        await SimulationCluster.RestartHostOfAsync(relayId);
        await reminder.ExpediteAsync(relayId, "db.outbox");

        OutboxRecoveryReceiver.Allow(receiverId);
        await AwaitIncomingAsync(receiverId, new OutboxRecoveryDelivered());
        await WaitForReminderRemovalAsync(reminder, relayId, "db.outbox");
    }

    [Fact(DisplayName = "a durable Task completes from typed facts emitted by its scripted Worker")]
    public async Task ATaskCompletesFromItsWorkerFacts()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-tracer");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var task = TaskFor(taskId);
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
        var task = TaskFor(taskId);
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
        var task = TaskFor(taskId);

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
        var task = TaskFor(taskId);
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
        var task = TaskFor(taskId);

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
        var task = TaskFor(taskId);
        var started = await task.StartAsync(new(
            CommandId.New(),
            new TracerGoal("retry"),
            workerId,
            new TaskPolicy(2, TimeSpan.FromSeconds(1), null)));

        var waiting = await ReadUntilAsync(
            task,
            snapshot => snapshot.State == TaskState.Waiting && snapshot.Blocker is RetryScheduled);
        var reminderId = NeuronId.For<ReminderProbe>(owner, "reminder");
        var reminder = SimulationCluster.Grains.GetGrain<IReminderProbe>(reminderId.ToGrainId());

        await WaitForReminderAsync(reminder, taskId, "tasks.retry");

        var succeeded = await ReadUntilAsync(task, snapshot => snapshot.State == TaskState.Succeeded);

        Assert.IsType<TracerFailure>(waiting.Failure);
        Assert.Equal(1, succeeded.Revision);
        var result = Assert.IsType<TracerResult>(succeeded.Result);
        Assert.StartsWith("retried:", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(started.ActiveAttempt!.Value.Value.ToString("D"), result.Value, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "an uncertain outcome waits without automatic retry")]
    public async Task UncertainOutcomeWaitsWithoutRetry()
    {
        await SimulationCluster.StartAsync();

        var owner = new OwnerId("task-uncertain");
        var taskId = NeuronId.For<ITask>(owner, "task");
        var workerId = NeuronId.For<ScriptedWorker>(owner, "worker");
        var blockerId = new BlockerId(Guid.NewGuid());
        var task = TaskFor(taskId);

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
        var task = TaskFor(taskId);

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
        var active = TaskFor(activeId);

        await active.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        await ReadUntilAsync(active, snapshot => snapshot.State == TaskState.Running);

        var refusedId = NeuronId.For<ITask>(owner, "refused-successor");
        var refused = TaskFor(refusedId);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => refused.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null),
            activeId)));

        var terminalId = NeuronId.For<ITask>(owner, "terminal");
        var terminal = TaskFor(terminalId);
        await terminal.StartAsync(new(
            CommandId.New(),
            new TracerGoal("complete"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null)));
        await ReadUntilAsync(terminal, snapshot => snapshot.State == TaskState.Succeeded);

        var successorId = NeuronId.For<ITask>(owner, "successor");
        var successor = TaskFor(successorId);
        var started = await successor.StartAsync(new(
            CommandId.New(),
            new TracerGoal("hold"),
            workerId,
            new TaskPolicy(1, TimeSpan.Zero, null),
            terminalId));

        Assert.Equal(terminalId, started.RetryOf);
        Assert.NotEqual(terminalId, successorId);
    }

    private static TaskTestClient TaskFor(NeuronId task)
    {
        var driverId = NeuronId.For<TaskDriver>(task.Owner, $"driver-{task.Name}");
        var driver = SimulationCluster.Grains.GetGrain<ITaskDriver>(driverId.ToGrainId());

        return new TaskTestClient(task, driver);
    }

    private static async Task<TaskSnapshot> ReadUntilAsync(
        TaskTestClient task,
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

    private static async Task WaitForReminderAsync(
        IReminderProbe reminder,
        NeuronId task,
        string reminderName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!await reminder.ExistsAsync(task, reminderName))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task WaitForReminderRemovalAsync(
        IReminderProbe reminder,
        NeuronId task,
        string reminderName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (await reminder.ExistsAsync(task, reminderName))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task WaitForOutboxAttemptAsync(NeuronId receiver)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!OutboxRecoveryReceiver.WasAttempted(receiver))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task AwaitIncomingAsync(NeuronId task, Synapse expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (true)
        {
            var journal = await Simulation.ReadJournalOfOwnerAsync(
                JournalKind.Incoming,
                task.Owner.Value,
                task.Type,
                task.Name,
                afterSequence: 0);

            if (journal.Delta.Any(delivery => delivery.Synapse == expected))
            {
                return;
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

    private static bool Is<TSynapse>(SynapseDelivery delivery)
        where TSynapse : Synapse
        => delivery.Synapse is TSynapse;
}

internal sealed class TaskTestClient(NeuronId task, ITaskDriver driver)
{
    public Task<TaskSnapshot> StartAsync(StartTask command) => driver.StartAsync(task, command);

    public Task<TaskSnapshot> CancelAsync(CancelTask command) => driver.CancelAsync(task, command);

    public Task<TaskSnapshot> ReadAsync() => driver.ReadAsync(task);
}

[Alias("db.test.task-driver")]
[ClientEntryPoint]
internal interface ITaskDriver : INeuron
{
    [Alias("StartTask")]
    Task<TaskSnapshot> StartAsync(NeuronId task, StartTask command);

    [Alias("CancelTask")]
    Task<TaskSnapshot> CancelAsync(NeuronId task, CancelTask command);

    [Alias("ReadTask")]
    Task<TaskSnapshot> ReadAsync(NeuronId task);
}

[GrainType("task-driver")]
internal sealed class TaskDriver : Neuron, ITaskDriver
{
    public Task<TaskSnapshot> StartAsync(NeuronId task, StartTask command)
        => Task(task).StartAsync(command);

    public Task<TaskSnapshot> CancelAsync(NeuronId task, CancelTask command)
        => Task(task).CancelAsync(command);

    public Task<TaskSnapshot> ReadAsync(NeuronId task)
        => Task(task).ReadAsync();

    private ITask Task(NeuronId task)
        => GrainFactory.GetGrain<ITask>(task.ToGrainId());
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
    IScriptedWorkerControl,
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
    private static readonly ConcurrentDictionary<(NeuronId Task, string Operation), int> Dispatches = new();

    internal static int DispatchCount(NeuronId task)
        => Dispatches.Where(entry => entry.Key.Task == task).Sum(entry => entry.Value);

    public Task SendFactAsync(AttemptFact fact) => SendAsync(fact.Task, fact);

    public async Task AcceptAsync(AttemptRequest request)
    {
        var description = ((TracerGoal)request.Goal).Description;
        Scripts[request.Task] = description;
        var dispatch = Dispatches.AddOrUpdate(
            (request.Task, nameof(AcceptAsync)),
            1,
            static (_, count) => count + 1);

        if (description == "recover-accept" && dispatch == 1)
        {
            throw new InvalidOperationException("scripted transient Accept failure");
        }

        if (description == "recover-accept")
        {
            await SendAsync(
                request.Task,
                new AttemptSucceeded(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new TracerResult("recovered accept"),
                    []));
            return;
        }

        if (description is "hold" or "cancel" or "progress-hold" or "recover-cancel"
            || description.StartsWith("cancel-", StringComparison.Ordinal)
            || description.StartsWith("invalid-", StringComparison.Ordinal))
        {
            await SendAsync(
                request.Task,
                new AttemptAccepted(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision));

            if (description == "invalid-blocker")
            {
                await SendAsync(
                    request.Task,
                    new AttemptWaiting(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        null!));
            }
            else if (description == "invalid-failure")
            {
                await SendAsync(
                    request.Task,
                    new AttemptFailed(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        null!,
                        Retryable: false));
            }
            else if (description == "invalid-result")
            {
                await SendAsync(
                    request.Task,
                    new AttemptSucceeded(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        null!,
                        []));
            }
            else if (description == "invalid-evidence")
            {
                await SendAsync(
                    request.Task,
                    new AttemptSucceeded(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        new TracerResult("invalid"),
                        null!));
            }

            return;
        }

        if (description == "fail")
        {
            await SendAsync(
                request.Task,
                new AttemptFailed(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new TracerFailure("non-retryable"),
                    Retryable: false));
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

        if (request.Goal is TracerGoal { Description: "advance" or "progress" or "recover-continue" })
        {
            await SendAsync(
                request.Task,
                new AttemptAccepted(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision));
            if (description == "advance")
            {
                await SendAsync(
                    request.Task,
                    new AttemptAdvanced(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision));
            }
            else
            {
                await SendAsync(
                    request.Task,
                    new AttemptProgressed(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision));
            }

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
        var dispatch = Dispatches.AddOrUpdate(
            (cursor.Task, nameof(ContinueAsync)),
            1,
            static (_, count) => count + 1);
        var script = Scripts[cursor.Task];

        if (script == "recover-continue" && dispatch == 1)
        {
            throw new InvalidOperationException("scripted transient Continue failure");
        }

        if (script == "progress-hold")
        {
            return;
        }

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
        var dispatch = Dispatches.AddOrUpdate(
            (cursor.Task, nameof(CancelAsync)),
            1,
            static (_, count) => count + 1);

        if (script == "recover-cancel" && dispatch == 1)
        {
            throw new InvalidOperationException("scripted transient Cancel failure");
        }

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

[Alias("db.test.scripted-worker-control")]
[ClientEntryPoint]
internal interface IScriptedWorkerControl : INeuron
{
    [Alias("SendFact")]
    Task SendFactAsync(AttemptFact fact);
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
    [Alias("Exists")]
    Task<bool> ExistsAsync(NeuronId task, string reminderName);

    [Alias("Expedite")]
    Task ExpediteAsync(NeuronId task, string reminderName);
}

internal sealed class ReminderProbe : Neuron, IReminderProbe
{
    private readonly Orleans.Timers.IReminderRegistry _reminders;

    public ReminderProbe()
    {
        _reminders = ServiceProvider.GetRequiredService<Orleans.Timers.IReminderRegistry>();
    }

    public async Task<bool> ExistsAsync(NeuronId task, string reminderName)
        => await _reminders.GetReminder(task.ToGrainId(), reminderName) is not null;

    public async Task ExpediteAsync(NeuronId task, string reminderName)
        => _ = await _reminders.RegisterOrUpdateReminder(
            task.ToGrainId(),
            reminderName,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1));
}

[GenerateSerializer]
[Alias("db.test.outbox-recovery-requested")]
internal sealed record OutboxRecoveryRequested : Synapse;

[GenerateSerializer]
[Alias("db.test.outbox-recovery-delivered")]
internal sealed record OutboxRecoveryDelivered : Synapse;

internal sealed class OutboxForwardingRelay :
    Neuron,
    IHandle<OutboxRecoveryRequested>,
    IRemindable
{
    public Task HandleAsync(OutboxRecoveryRequested synapse, CancellationToken cancellationToken)
        => SendAsync(
            NeuronId.For<OutboxRecoveryReceiver>(Id.Owner, "receiver"),
            new OutboxRecoveryDelivered());

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
        => await base.ReceiveReminder(reminderName, status);
}

internal sealed class OutboxRecoveryReceiver : Neuron, IHandle<OutboxRecoveryDelivered>
{
    private static readonly ConcurrentDictionary<NeuronId, bool> Allowed = new();

    internal static void Allow(NeuronId receiver) => Allowed[receiver] = true;

    internal static bool WasAttempted(NeuronId receiver) => Allowed.ContainsKey(receiver);

    public Task HandleAsync(OutboxRecoveryDelivered synapse, CancellationToken cancellationToken)
    {
        if (!Allowed.TryGetValue(Id, out var allowed))
        {
            Allowed[Id] = false;
            throw new InvalidOperationException("The receiver is unavailable until the outbox sender restarts.");
        }

        if (!allowed)
        {
            throw new InvalidOperationException("The receiver is unavailable until the outbox sender restarts.");
        }

        Allowed.TryRemove(Id, out _);
        return Task.CompletedTask;
    }
}
