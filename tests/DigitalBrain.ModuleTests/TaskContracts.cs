using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class TaskContracts(ModuleFixture fixture)
{
    [Fact]
    public async Task StartCreatesOnePendingAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("driver");
        var task = test.Neuron<ITask>("start");
        var worker = test.Neuron<IScriptedWorker>("worker");

        var started = await Start(
            test,
            driver,
            task,
            worker.Id,
            "hold",
            retryOf: null,
            cancellationToken);

        Assert.Equal(TaskState.Pending, started.State);
        Assert.NotNull(started.ActiveAttempt);
        Assert.Equal(0, started.Revision);
    }

    [Fact]
    public async Task WaitingPreservesTheTypedBlocker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("driver");
        var task = test.Neuron<ITask>("waiting");
        var worker = test.Neuron<IScriptedWorker>("worker");
        var waiting = task.Incoming.NextAsync<AttemptWaiting>(
            cancellationToken);

        _ = await Start(
            test,
            driver,
            task,
            worker.Id,
            "wait",
            retryOf: null,
            cancellationToken);
        var fact = await waiting;
        var snapshot = await Read(
            test,
            driver,
            task,
            cancellationToken);

        Assert.Equal(TaskState.Waiting, snapshot.State);
        Assert.Equal(fact.Synapse.Blocker, snapshot.Blocker);
        Assert.IsType<InputRequired>(snapshot.Blocker);
    }

    [Fact]
    public async Task ProgressAdvancesTheDurableRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("driver");
        var task = test.Neuron<ITask>("progress");
        var worker = test.Neuron<IScriptedWorker>("worker");
        var progressed = task.Incoming.NextAsync<AttemptProgressed>(
            cancellationToken);

        _ = await Start(
            test,
            driver,
            task,
            worker.Id,
            "progress",
            retryOf: null,
            cancellationToken);
        var fact = await progressed;
        var snapshot = await Read(
            test,
            driver,
            task,
            cancellationToken);

        Assert.Equal(0, fact.Synapse.Revision);
        Assert.Equal(1, snapshot.Revision);
    }

    [Fact]
    public async Task SuccessPublishesTheTypedResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("driver");
        var task = test.Neuron<ITask>("success");
        var worker = test.Neuron<IScriptedWorker>("worker");
        var succeeded = task.Incoming.NextAsync<AttemptSucceeded>(
            cancellationToken);

        _ = await Start(
            test,
            driver,
            task,
            worker.Id,
            "success",
            retryOf: null,
            cancellationToken);
        await succeeded;
        var snapshot = await Read(
            test,
            driver,
            task,
            cancellationToken);

        Assert.Equal(TaskState.Succeeded, snapshot.State);
        Assert.Equal(new ModuleResult("done"), snapshot.Result);
        Assert.Null(snapshot.ActiveAttempt);
    }

    [Fact]
    public async Task FailurePublishesTheTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("driver");
        var task = test.Neuron<ITask>("failure");
        var worker = test.Neuron<IScriptedWorker>("worker");
        var failed = task.Incoming.NextAsync<AttemptFailed>(
            cancellationToken);

        _ = await Start(
            test,
            driver,
            task,
            worker.Id,
            "failure",
            retryOf: null,
            cancellationToken);
        await failed;
        var snapshot = await Read(
            test,
            driver,
            task,
            cancellationToken);

        Assert.Equal(TaskState.Failed, snapshot.State);
        Assert.Equal(
            new ModuleFailure("expected failure"),
            snapshot.Failure);
    }

    [Fact]
    public async Task CancellationCommitsTheWinningOutcome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("driver");
        var task = test.Neuron<ITask>("cancellation");
        var worker = test.Neuron<IScriptedWorker>("worker");
        var accepted = task.Incoming.NextAsync<AttemptAccepted>(
            cancellationToken);

        _ = await Start(
            test,
            driver,
            task,
            worker.Id,
            "cancel",
            retryOf: null,
            cancellationToken);
        await accepted;
        var running = await Read(
            test,
            driver,
            task,
            cancellationToken);
        var cancelled = task.Incoming.NextAsync<AttemptCancelled>(
            cancellationToken);
        var cancelling = driver.Outgoing.NextAsync<TaskObserved>(
            cancellationToken);

        await test.Client.SendAsync<IModuleDriver>(
            "driver",
            new CancelModuleTask(
                task.Id,
                new CancelTask(
                    CommandId.New(),
                    running.Revision)));
        var requested = await cancelling;
        await cancelled;
        var terminal = await Read(
            test,
            driver,
            task,
            cancellationToken);

        Assert.Equal(TaskState.Cancelling, requested.Synapse.Snapshot.State);
        Assert.Equal(TaskState.Cancelled, terminal.State);
    }

    [Fact]
    public async Task RetryIsANewSuccessorOfATerminalTask()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.ConfigureModuleParameters();
        var driver = test.Neuron<IModuleDriver>("driver");
        var worker = test.Neuron<IScriptedWorker>("worker");
        var predecessor = test.Neuron<ITask>("predecessor");
        var terminal = predecessor.Incoming.NextAsync<AttemptSucceeded>(
            cancellationToken);

        _ = await Start(
            test,
            driver,
            predecessor,
            worker.Id,
            "success",
            retryOf: null,
            cancellationToken);
        await terminal;

        var successor = test.Neuron<ITask>("successor");
        var started = await Start(
            test,
            driver,
            successor,
            worker.Id,
            "hold",
            predecessor.Id,
            cancellationToken);

        Assert.Equal(predecessor.Id, started.RetryOf);
        Assert.NotEqual(predecessor.Id, successor.Id);
    }

    private static async Task<TaskSnapshot> Start(
        TestBrain test,
        TestNeuron<IModuleDriver> driver,
        TestNeuron<ITask> task,
        NeuronId worker,
        string script,
        NeuronId? retryOf,
        CancellationToken cancellationToken)
    {
        var observed = driver.Outgoing.NextAsync<TaskObserved>(
            cancellationToken);
        await test.Client.SendAsync<IModuleDriver>(
            "driver",
            new StartModuleTask(
                task.Id,
                new StartTask(
                    CommandId.New(),
                    new ModuleGoal(script),
                    worker,
                    new TaskPolicy(1, TimeSpan.Zero, null),
                    retryOf)));
        var result = await observed;

        Assert.Equal(nameof(ITask.Start), result.Synapse.Operation);
        return result.Synapse.Snapshot;
    }

    private static async Task<TaskSnapshot> Read(
        TestBrain test,
        TestNeuron<IModuleDriver> driver,
        TestNeuron<ITask> task,
        CancellationToken cancellationToken)
    {
        var observed = driver.Outgoing.NextAsync<TaskObserved>(
            cancellationToken);
        await test.Client.SendAsync<IModuleDriver>(
            "driver",
            new ReadModuleTask(task.Id));
        var result = await observed;

        Assert.Equal(nameof(ITask.Read), result.Synapse.Operation);
        return result.Synapse.Snapshot;
    }
}
