using DigitalBrain.Abstractions;
using DigitalBrain.Execution;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ExecutionSpikeProofs(BrainClusterFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task DuplicateStartCommandIdReturnsTheSameExecutionReceipt()
    {
        var brain = fixture.BrainFor("exec-dup");
        var worker = WorkerId(brain, "dup-worker");
        HarnessWorkerControl.Configure("dup-worker", HarnessWorkerScript.SucceedOnAccept);

        var commandId = CommandId.New();
        var start = new ApplyExecution(
            commandId,
            new StartExecution(
                new ProbeGoal("once"),
                worker,
                new ExecutionPolicy(MaximumAttempts: 1, RetryDelay: TimeSpan.FromSeconds(1), Deadline: null)));

        var first = await brain.Get<IExecution>("dup-run").FireAsync(start, TestContext.Current.CancellationToken);
        var second = await brain.Get<IExecution>("dup-run").FireAsync(start, TestContext.Current.CancellationToken);

        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(first.ActiveAttempt, second.ActiveAttempt);
        Assert.Equal(first.State, second.State);
        Assert.Equal(1, HarnessWorkerControl.AcceptCount("dup-worker"));
    }

    [Fact]
    public async Task ExplicitCancelReachesCancelledThroughTheWorker()
    {
        var brain = fixture.BrainFor("exec-cancel");
        var worker = WorkerId(brain, "cancel-worker");
        HarnessWorkerControl.Configure("cancel-worker", HarnessWorkerScript.CancelAware);

        var started = await brain.Get<IExecution>("cancel-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new StartExecution(
                    new ProbeGoal("cancel-me"),
                    worker,
                    new ExecutionPolicy(1, TimeSpan.FromSeconds(1), null))),
            TestContext.Current.CancellationToken);

        await WaitForStateAsync(brain, "cancel-run", ExecutionState.Running);

        var cancelled = await brain.Get<IExecution>("cancel-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new CancelExecution(),
                ExpectedRevision: started.Revision),
            TestContext.Current.CancellationToken);

        Assert.True(
            cancelled.State is ExecutionState.Cancelling or ExecutionState.Cancelled,
            $"Expected cancelling/cancelled, got {cancelled.State}");

        await WaitForStateAsync(brain, "cancel-run", ExecutionState.Cancelled);
    }

    [Fact]
    public async Task OauthStyleBlockerWaitThenResumeCompletes()
    {
        var brain = fixture.BrainFor("exec-oauth");
        var worker = WorkerId(brain, "oauth-worker");
        HarnessWorkerControl.Configure("oauth-worker", HarnessWorkerScript.WaitForOauth);

        await brain.Get<IExecution>("oauth-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new StartExecution(
                    new ProbeGoal("oauth"),
                    worker,
                    new ExecutionPolicy(1, TimeSpan.FromSeconds(1), null))),
            TestContext.Current.CancellationToken);

        await WaitForStateAsync(brain, "oauth-run", ExecutionState.Waiting);
        var waiting = await brain.GetGrainProxy<IExecution>("oauth-run").Read();
        Assert.IsType<InputRequired>(waiting.Blocker);

        await brain.FireAsync(worker, new ResumeWorkerBlocker("oauth"), TestContext.Current.CancellationToken);

        await WaitForStateAsync(brain, "oauth-run", ExecutionState.Succeeded);
        var done = await brain.GetGrainProxy<IExecution>("oauth-run").Read();
        Assert.IsType<ProbeResult>(done.Result);
    }

    [Fact]
    public async Task UncertainExternalWriteBlocksWithoutAutoRetryThenResolves()
    {
        var brain = fixture.BrainFor("exec-uncertain");
        var worker = WorkerId(brain, "uncertain-worker");
        HarnessWorkerControl.Configure("uncertain-worker", HarnessWorkerScript.UncertainExternalWrite);

        var started = await brain.Get<IExecution>("uncertain-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new StartExecution(
                    new ProbeGoal("write"),
                    worker,
                    new ExecutionPolicy(MaximumAttempts: 3, RetryDelay: TimeSpan.FromMilliseconds(50), Deadline: null))),
            TestContext.Current.CancellationToken);

        await WaitForStateAsync(brain, "uncertain-run", ExecutionState.Waiting);
        var blocked = await brain.GetGrainProxy<IExecution>("uncertain-run").Read();
        Assert.IsType<OutcomeUncertain>(blocked.Blocker);

        // Stay blocked across a window that would have fired a short retry timer.
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        var stillBlocked = await brain.GetGrainProxy<IExecution>("uncertain-run").Read();
        Assert.IsType<OutcomeUncertain>(stillBlocked.Blocker);
        Assert.Equal(started.AttemptCount, stillBlocked.AttemptCount);

        var resolved = await brain.Get<IExecution>("uncertain-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new ResolveOperation(
                    "external-write",
                    OperationResolution.Completed,
                    new ProtectedPayloadReference(Guid.NewGuid()),
                    "reconciled by operator"),
                ExpectedRevision: stillBlocked.Revision),
            TestContext.Current.CancellationToken);

        Assert.Null(resolved.Blocker);
        Assert.True(
            resolved.State is ExecutionState.Running or ExecutionState.Succeeded,
            $"Expected running/succeeded after resolve, got {resolved.State}");

        await WaitForStateAsync(brain, "uncertain-run", ExecutionState.Succeeded);
    }

    [Fact]
    public async Task DispatchedRetryableFailForcesOutcomeUncertainWithoutAutoRetry()
    {
        var brain = fixture.BrainFor("exec-dispatch-fail");
        var worker = WorkerId(brain, "dispatch-fail-worker");
        HarnessWorkerControl.Configure("dispatch-fail-worker", HarnessWorkerScript.DispatchThenRetryableFail);

        var started = await brain.Get<IExecution>("dispatch-fail-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new StartExecution(
                    new ProbeGoal("messy"),
                    worker,
                    new ExecutionPolicy(
                        MaximumAttempts: 3,
                        RetryDelay: TimeSpan.FromMilliseconds(50),
                        Deadline: null))),
            TestContext.Current.CancellationToken);

        await WaitForStateAsync(brain, "dispatch-fail-run", ExecutionState.Waiting);
        var blocked = await brain.GetGrainProxy<IExecution>("dispatch-fail-run").Read();
        Assert.IsType<OutcomeUncertain>(blocked.Blocker);
        Assert.Equal(started.AttemptCount, blocked.AttemptCount);

        // Reminder must not open a new attempt while Dispatched work is unresolved.
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        var stillBlocked = await brain.GetGrainProxy<IExecution>("dispatch-fail-run").Read();
        Assert.IsType<OutcomeUncertain>(stillBlocked.Blocker);
        Assert.Equal(started.AttemptCount, stillBlocked.AttemptCount);
        Assert.Equal(1, HarnessWorkerControl.AcceptCount("dispatch-fail-worker"));

        var read = await brain.Get<IExecution>("dispatch-fail-run").FireAsync(
            new ReadOperation("messy-write"),
            TestContext.Current.CancellationToken);
        Assert.NotNull(read.Operation);
        Assert.Equal(OperationPhase.Uncertain, read.Operation!.Phase);
    }

    [Fact]
    public async Task OperationKeyIsAttemptStableAcrossRetryableFailure()
    {
        var brain = fixture.BrainFor("exec-stable-op");
        var worker = WorkerId(brain, "stable-worker");
        HarnessWorkerControl.Configure("stable-worker", HarnessWorkerScript.CompleteThenRetryableFail);

        await brain.Get<IExecution>("stable-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new StartExecution(
                    new ProbeGoal("stable"),
                    worker,
                    new ExecutionPolicy(
                        MaximumAttempts: 2,
                        RetryDelay: TimeSpan.FromMilliseconds(100),
                        Deadline: null))),
            TestContext.Current.CancellationToken);

        // First attempt fails retryably after completing stable-write; reminder starts attempt 2.
        // Second accept re-Prepares the same key (adversarial) then succeeds without re-effect.
        await WaitForStateAsync(brain, "stable-run", ExecutionState.Succeeded);

        var read = await brain.Get<IExecution>("stable-run").FireAsync(
            new ReadOperation("stable-write"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(read.Operation);
        Assert.Equal("stable-write", read.Operation!.OperationKey);
        Assert.Equal(OperationPhase.Completed, read.Operation.Phase);

        Assert.True(
            HarnessWorkerControl.PrepareCount("stable-worker") >= 2,
            "Second attempt must re-Prepare the Completed key (kernel short-circuits to recorded completion).");
        Assert.Equal(1, HarnessWorkerControl.ExternalEffectCount("stable-worker"));
        Assert.True(
            HarnessWorkerControl.AcceptCount("stable-worker") >= 2,
            "Retry must open a second Accept after Completed+retryable fail.");
    }

    [Fact]
    public async Task ExecutionStateSurvivesSiloRestart()
    {
        var brain = fixture.BrainFor("exec-restart");
        var worker = WorkerId(brain, "restart-worker");
        HarnessWorkerControl.Configure("restart-worker", HarnessWorkerScript.WaitForOauth);

        var started = await brain.Get<IExecution>("restart-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new StartExecution(
                    new ProbeGoal("durable"),
                    worker,
                    new ExecutionPolicy(1, TimeSpan.FromSeconds(1), null))),
            TestContext.Current.CancellationToken);

        await WaitForStateAsync(brain, "restart-run", ExecutionState.Waiting);
        var before = await brain.GetGrainProxy<IExecution>("restart-run").Read();

        await fixture.RestartSilosAsync();

        // Client may need a moment to rebind after dual-silo restart.
        ExecutionSnapshot? after = null;
        await WaitForAsync(async () =>
        {
            try
            {
                after = await brain.GetGrainProxy<IExecution>("restart-run").Read();
                return after.State == before.State;
            }
            catch (Exception)
            {
                return false;
            }
        });

        Assert.NotNull(after);
        Assert.Equal(before.State, after!.State);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.ActiveAttempt, after.ActiveAttempt);
        Assert.Equal(started.Goal, after.Goal);
        Assert.IsType<InputRequired>(after.Blocker);
    }

    [Fact]
    public void ExecutionManifestUsesDbExecutionAliases()
    {
        var manifest = DigitalBrain.Core.ModuleReflection.ManifestOf(typeof(IExecution).Assembly);
        var execution = Assert.Single(manifest.Neurons, neuron => neuron.ContractId == "db.execution");
        Assert.Contains(execution.Accepted, synapse => synapse.ContractId == "db.execution.apply");
        Assert.Contains(execution.Emitted, synapse => synapse.ContractId == "db.execution.snapshot");
        Assert.Contains(manifest.Facts, fact => fact.ContractId == "db.execution.attempt-outcome-uncertain");
        Assert.Contains(manifest.Facts, fact => fact.ContractId == "db.execution.prepare-operation");
        Assert.Contains(manifest.Facts, fact => fact.ContractId == "db.execution.apply");
    }

    private static NeuronId WorkerId(Client.IDigitalBrain brain, string name)
        => new(NeuronId.GrainTypeNameOf(typeof(IWorker)), brain.Owner, name);

    private static async Task WaitForStateAsync(
        Client.IDigitalBrain brain,
        string executionName,
        ExecutionState expected)
    {
        await WaitForAsync(async () =>
        {
            var snap = await brain.GetGrainProxy<IExecution>(executionName).Read();
            return snap.State == expected;
        });
    }

    private static async Task WaitForAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Condition not met within {Patience}.");
    }
}
