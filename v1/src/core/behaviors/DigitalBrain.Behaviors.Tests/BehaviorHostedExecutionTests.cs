using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorHostedExecutionTests(BehaviorHostedExecutionFixture fixture)
{
    [Fact(
        Timeout = 90_000,
        DisplayName = "Worker Accept stages hosted execution via relay; Task Succeeded with stable code; no legacy Execute")]
    public async Task WorkerAcceptInvokesHardenedExecutorAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        fixture.Executor.Reset(succeeded: true, outcome: "secret-provider-output-must-not-journal");

        var worker = brain.Neuron<IWorker>("hosted-success-worker");
        var task = brain.Neuron<ITask>("hosted-success-task");
        var triggerRef = new ProtectedPayloadReference(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var capabilities = new TaskOperationEdge[]
        {
            new(
                new NeuronId("test.capability", task.Id.Owner, "named"),
                "test.req",
                1,
                "test.res",
                1),
        };
        var activation = new BehaviorTaskActivation(
            new BehaviorId(BehaviorsFixture.SampleBehavior),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "case.SampleTrigger",
            protectedPayload: triggerRef,
            triggerTypeName: "SampleTrigger",
            capabilities: capabilities);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));

        var accepted = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, accepted.Synapse.Attempt);

        var succeeded = await task.Incoming.NextAsync<AttemptSucceeded>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, succeeded.Synapse.Attempt);
        var result = Assert.IsType<BehaviorTaskResult>(succeeded.Synapse.Result);
        Assert.Equal(BehaviorExecutionCodes.Succeeded, result.Outcome);
        Assert.DoesNotContain("secret-provider-output", result.Outcome, StringComparison.Ordinal);
        Assert.Empty(succeeded.Synapse.Evidence);

        var terminal = await WaitForStateAsync(task, TaskState.Succeeded, cancellationToken);
        Assert.Equal(result, terminal.Result);
        Assert.Null(terminal.Failure);
        Assert.DoesNotContain("secret-provider-output", terminal.ToString(), StringComparison.Ordinal);

        Assert.Equal(1, fixture.Executor.HardenedCalls);
        Assert.Equal(0, fixture.Executor.LegacyCalls);
        var request = Assert.Single(fixture.Executor.HardenedRequests.ToArray());
        Assert.Equal(task.Id.Owner, request.Metadata.Owner);
        Assert.Equal(activation.BehaviorId, request.Metadata.Behavior);
        Assert.Equal(activation.Revision, request.Metadata.Revision);
        Assert.Equal(task.Id, request.Task);
        Assert.Equal(started.ActiveAttempt, request.Attempt);
        Assert.Equal(worker.Id, request.Worker);
        Assert.Equal("SampleTrigger", request.TriggerTypeName);
        Assert.Equal(triggerRef, request.TriggerPayload);
        Assert.True(request.ArtifactBytes.IsEmpty);
        Assert.Equal(activation.Revision.Value, request.ArtifactHash);
        Assert.Equal(capabilities.Length, request.Capabilities.Count);
        Assert.Equal(capabilities[0].Target, request.Capabilities[0].Target);
        Assert.Equal(capabilities[0].RequestSynapseId, request.Capabilities[0].RequestSynapseId);
        Assert.NotEqual(default, request.UtcNow);
    }

    [Fact(
        Timeout = 90_000,
        DisplayName = "Worker Accept maps executor failure to stable Behavior failure without raw payload leakage")]
    public async Task WorkerAcceptMapsExecutorFailureToRedactedBehaviorFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string secret = "super-secret-trigger-bytes";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        fixture.Executor.Reset(
            succeeded: false,
            outcome: $"execution failed while handling {secret} and more diagnostic text that should stay bounded " +
                     new string('x', 300));

        var worker = brain.Neuron<IWorker>("hosted-fail-worker");
        var task = brain.Neuron<ITask>("hosted-fail-task");
        var triggerRef = new ProtectedPayloadReference(Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"));
        var activation = new BehaviorTaskActivation(
            new BehaviorId(BehaviorsFixture.SampleBehavior),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "case.SampleTrigger",
            protectedPayload: triggerRef,
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var failed = await task.Incoming.NextAsync<AttemptFailed>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, failed.Synapse.Attempt);
        Assert.False(failed.Synapse.Retryable);
        var failure = Assert.IsType<BehaviorTaskFailure>(failed.Synapse.Failure);
        Assert.Equal(BehaviorExecutionCodes.Failed, failure.Reason);
        Assert.DoesNotContain(secret, failure.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, failed.ToString(), StringComparison.Ordinal);

        var terminal = await WaitForStateAsync(task, TaskState.Failed, cancellationToken);
        Assert.Equal(failure, terminal.Failure);
        Assert.Null(terminal.Result);
        Assert.Equal(1, fixture.Executor.HardenedCalls);
        Assert.Equal(0, fixture.Executor.LegacyCalls);
        Assert.DoesNotContain(secret, terminal.ToString(), StringComparison.Ordinal);
    }

    [Fact(
        Timeout = 90_000,
        DisplayName =
            "hosted Accept stages execution so reverse-broker StageDispatch + capability callback complete without Worker deadlock")]
    public async Task HostedAcceptWithCapabilityCallbackCompletesWithoutDeadlock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var probeText = $"deadlock-free-{Guid.NewGuid():N}";
        var worker = brain.Neuron<IWorker>("deadlock-worker");
        var task = brain.Neuron<ITask>("deadlock-task");
        var probe = brain.Neuron<IDispatchProbe>("deadlock-probe");
        var edge = new BehaviorCapabilityEdge(
            new NeuronId(DispatchHarness.NeuronContractId, task.Id.Owner, probe.Id.Name),
            DispatchHarness.RequestContractId,
            1,
            DispatchHarness.ResponseContractId,
            1);
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);

        // Hold hosted execution open on the relay turn until StageDispatch proves the Worker is free.
        var hostedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Executor.Reset(succeeded: true, outcome: BehaviorExecutionCodes.Succeeded);
        fixture.Executor.OnHardened = async (request, token) =>
        {
            await hostedGate.Task.WaitAsync(token);
            var requestRef = await payloads.StoreAsync(
                request.Metadata.Owner,
                request.Task,
                request.Attempt,
                DispatchHarness.SerializeRequest(probeText),
                token);
            _ = await dispatch.DispatchAsync(
                request.Metadata.Owner,
                request.Task,
                request.Attempt,
                edge,
                requestRef,
                token);
            return new BehaviorExecutionOutcome(true, BehaviorExecutionCodes.Succeeded);
        };

        var activation = new BehaviorTaskActivation(
            new BehaviorId(BehaviorsFixture.SampleBehavior),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "case.SampleTrigger",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("cccccccc-dddd-eeee-ffff-000000000001")),
            triggerTypeName: "SampleTrigger",
            capabilities:
            [
                new TaskOperationEdge(
                    edge.Target,
                    edge.RequestSynapseId,
                    edge.RequestSchemaVersion,
                    edge.ResponseSynapseId,
                    edge.ResponseSchemaVersion),
            ]);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation))
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var running = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(started.ActiveAttempt, running.ActiveAttempt);

        // While hosted execution is still in-flight on the relay, reverse-broker StageDispatch
        // must enter the non-reentrant Worker and deliver the capability probe.
        var midFlightRef = await payloads.StoreAsync(
            task.Id.Owner,
            task.Id,
            started.ActiveAttempt!.Value,
            DispatchHarness.SerializeRequest(probeText + "-mid"),
            cancellationToken);
        _ = await dispatch.DispatchAsync(
            task.Id.Owner,
            task.Id,
            started.ActiveAttempt.Value,
            edge,
            midFlightRef,
            cancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        Assert.Equal(1, DispatchHarness.CountFor(probeText + "-mid"));

        hostedGate.SetResult();
        var succeeded = await task.Incoming.NextAsync<AttemptSucceeded>(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        Assert.Equal(started.ActiveAttempt, succeeded.Synapse.Attempt);
        var result = Assert.IsType<BehaviorTaskResult>(succeeded.Synapse.Result);
        Assert.Equal(BehaviorExecutionCodes.Succeeded, result.Outcome);

        var terminal = await WaitForStateAsync(task, TaskState.Succeeded, cancellationToken);
        Assert.Equal(TaskState.Succeeded, terminal.State);
        Assert.Equal(1, fixture.Executor.HardenedCalls);
        Assert.Equal(1, DispatchHarness.CountFor(probeText));
        fixture.Executor.OnHardened = null;
    }

    [Fact(
        Timeout = 90_000,
        DisplayName = "duplicate CompleteHostedBehaviorExecution does not double-apply terminal Task outcome")]
    public async Task DuplicateTerminalDeliveryDoesNotDoubleApply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        fixture.Executor.Reset(succeeded: true, outcome: BehaviorExecutionCodes.Succeeded);

        var worker = brain.Neuron<IWorker>("dup-terminal-worker");
        var task = brain.Neuron<ITask>("dup-terminal-task");
        var activation = new BehaviorTaskActivation(
            new BehaviorId(BehaviorsFixture.SampleBehavior),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "case.SampleTrigger",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("dddddddd-eeee-ffff-0000-111111111111")),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        _ = await task.Incoming.NextAsync<AttemptSucceeded>(cancellationToken);
        var terminal = await WaitForStateAsync(task, TaskState.Succeeded, cancellationToken);
        Assert.NotNull(terminal.Result);

        var attempt = new AttemptRequest(
            task.Id,
            worker.Id,
            started.ActiveAttempt!.Value,
            started.Revision,
            goal);
        await brain.Client
            .SendAsync(
                worker.Id,
                new CompleteHostedBehaviorExecution(
                    attempt,
                    Succeeded: true,
                    BehaviorExecutionCodes.Succeeded,
                    Cancelled: false),
                cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        var after = await task.Reference.Read();
        Assert.Equal(TaskState.Succeeded, after.State);
        Assert.Equal(terminal.Result, after.Result);
        Assert.Equal(1, fixture.Executor.HardenedCalls);
    }

    [Fact(
        Timeout = 90_000,
        DisplayName = "executor cancellation is classified as stable cancelled failure, not success or generic failure")]
    public async Task ExecutorCancellationIsClassifiedAsCancelled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        fixture.Executor.Reset(succeeded: false, outcome: BehaviorExecutionCodes.Cancelled, throwCancel: true);

        var worker = brain.Neuron<IWorker>("cancel-class-worker");
        var task = brain.Neuron<ITask>("cancel-class-task");
        var activation = new BehaviorTaskActivation(
            new BehaviorId(BehaviorsFixture.SampleBehavior),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "case.SampleTrigger",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("eeeeeeee-ffff-0000-1111-222222222222")),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var failed = await task.Incoming.NextAsync<AttemptFailed>(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        Assert.Equal(started.ActiveAttempt, failed.Synapse.Attempt);
        var failure = Assert.IsType<BehaviorTaskFailure>(failed.Synapse.Failure);
        Assert.Equal(BehaviorExecutionCodes.Cancelled, failure.Reason);
        Assert.NotEqual(BehaviorExecutionCodes.Failed, failure.Reason);
        Assert.NotEqual(BehaviorExecutionCodes.Succeeded, failure.Reason);

        var terminal = await WaitForStateAsync(task, TaskState.Failed, cancellationToken);
        Assert.Equal(failure, terminal.Failure);
        Assert.Null(terminal.Result);
    }

    [Fact(DisplayName = "InProcess hardened ExecuteAsync remains closed")]
    public async Task InProcessHardenedExecuteAsyncRemainsClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var executor = new InProcessBehaviorExecutor();
        var outcome = await executor.ExecuteAsync(
            new BehaviorExecutionRequest(
                new BehaviorExecutionMetadata(
                    new OwnerId("inprocess-owner"),
                    new BehaviorId("com.digitalbrain.sample"),
                    new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
                    BehaviorExecutionId.New()),
                ArtifactBytes: ReadOnlyMemory<byte>.Empty,
                ArtifactHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                NeuronId.For<ITask>(new OwnerId("inprocess-owner"), "t"),
                new AttemptId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                TriggerTypeName: "SampleTrigger",
                new ProtectedPayloadReference(Guid.Parse("22222222-2222-2222-2222-222222222222")),
                Capabilities: [],
                DateTimeOffset.UtcNow,
                NeuronId.For<IWorker>(new OwnerId("inprocess-owner"), "w")),
            cancellationToken);

        Assert.False(outcome.Succeeded);
        Assert.Equal(BehaviorExecutionCodes.InProcessClosed, outcome.Outcome);
        Assert.Contains("isolated host/broker", outcome.Outcome, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TaskSnapshot> WaitForStateAsync(
        TestNeuron<ITask> task,
        TaskState expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await task.Reference.Read();
            if (snapshot.State == expected)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        var final = await task.Reference.Read();
        throw new TimeoutException($"Task stayed in {final.State} instead of {expected}.");
    }
}

public sealed class BehaviorHostedExecutionFixture : DigitalBrainFixture
{
    public RecordingBehaviorExecutor Executor { get; } = new();

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<TasksModule>();
        brain.AddModule<BehaviorDispatchHarnessModule>();

        var recording = Executor;
        brain.ConfigureServiceEdge(
            services =>
            {
                services.RemoveAll<IBehaviorExecutor>();
                services.AddSingleton<IBehaviorExecutor>(recording);
            },
            recording,
            static recorded => recorded.Reset(succeeded: true, outcome: "reset"));
    }
}

public sealed class RecordingBehaviorExecutor : IBehaviorExecutor
{
    private readonly Lock gate = new();
    private bool succeeded = true;
    private string outcome = "ok";
    private bool throwCancel;

    public int HardenedCalls { get; private set; }

    public int LegacyCalls { get; private set; }

    public Func<BehaviorExecutionRequest, CancellationToken, ValueTask<BehaviorExecutionOutcome>>? OnHardened
    {
        get;
        set;
    }

    public IReadOnlyList<BehaviorExecutionRequest> HardenedRequests
    {
        get
        {
            lock (gate)
            {
                return [.. hardenedRequests];
            }
        }
    }

    private readonly List<BehaviorExecutionRequest> hardenedRequests = [];

    public void Reset(bool succeeded, string outcome, bool throwCancel = false)
    {
        lock (gate)
        {
            this.succeeded = succeeded;
            this.outcome = outcome;
            this.throwCancel = throwCancel;
            HardenedCalls = 0;
            LegacyCalls = 0;
            hardenedRequests.Clear();
            OnHardened = null;
        }
    }

    public async ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        Func<BehaviorExecutionRequest, CancellationToken, ValueTask<BehaviorExecutionOutcome>>? handler;
        lock (gate)
        {
            HardenedCalls++;
            hardenedRequests.Add(request);
            handler = OnHardened;
            if (throwCancel)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        if (handler is not null)
        {
            return await handler(request, cancellationToken);
        }

        return new BehaviorExecutionOutcome(succeeded, outcome);
    }

    public ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            LegacyCalls++;
            return ValueTask.FromResult(new BehaviorExecutionOutcome(false, "legacy-not-used"));
        }
    }
}
