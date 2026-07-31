using DigitalBrain.Abstractions;
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
        DisplayName = "Worker Accept invokes recording hardened executor once; Task Succeeded with Behavior result; no legacy Execute")]
    public async Task WorkerAcceptInvokesHardenedExecutorAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        fixture.Executor.Reset(succeeded: true, outcome: "hosted-ok");

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
        Assert.Equal("hosted-ok", result.Outcome);
        Assert.Empty(succeeded.Synapse.Evidence);

        var terminal = await WaitForStateAsync(task, TaskState.Succeeded, cancellationToken);
        Assert.Equal(result, terminal.Result);
        Assert.Null(terminal.Failure);

        Assert.Equal(1, fixture.Executor.HardenedCalls);
        Assert.Equal(0, fixture.Executor.LegacyCalls);
        var request = Assert.Single(fixture.Executor.HardenedRequests.ToArray());
        Assert.Equal(task.Id.Owner, request.Metadata.Owner);
        Assert.Equal(activation.BehaviorId, request.Metadata.Behavior);
        Assert.Equal(activation.Revision, request.Metadata.Revision);
        Assert.Equal(task.Id, request.Task);
        Assert.Equal(started.ActiveAttempt, request.Attempt);
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
        DisplayName = "Worker Accept maps executor failure to redacted Behavior failure and Task Failed without raw payload leakage")]
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
        Assert.Equal("behavior-execution-failed", failure.Reason);
        Assert.DoesNotContain(secret, failure.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, failed.ToString(), StringComparison.Ordinal);

        var terminal = await WaitForStateAsync(task, TaskState.Failed, cancellationToken);
        Assert.Equal(failure, terminal.Failure);
        Assert.Null(terminal.Result);
        Assert.Equal(1, fixture.Executor.HardenedCalls);
        Assert.Equal(0, fixture.Executor.LegacyCalls);
        Assert.DoesNotContain(secret, terminal.ToString(), StringComparison.Ordinal);
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
                DateTimeOffset.UtcNow),
            cancellationToken);

        Assert.False(outcome.Succeeded);
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
        var executor = Executor;
        brain.ConfigureServiceEdge(
            services =>
            {
                services.RemoveAll<IBehaviorExecutor>();
                services.AddSingleton<IBehaviorExecutor>(executor);
            },
            executor,
            static recorded => recorded.Reset(succeeded: true, outcome: "reset"));
    }
}

public sealed class RecordingBehaviorExecutor : IBehaviorExecutor
{
    private readonly Lock gate = new();
    private bool succeeded = true;
    private string outcome = "ok";

    public int HardenedCalls { get; private set; }

    public int LegacyCalls { get; private set; }

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

    public void Reset(bool succeeded, string outcome)
    {
        lock (gate)
        {
            this.succeeded = succeeded;
            this.outcome = outcome;
            HardenedCalls = 0;
            LegacyCalls = 0;
            hardenedRequests.Clear();
        }
    }

    public ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            HardenedCalls++;
            hardenedRequests.Add(request);
            return ValueTask.FromResult(new BehaviorExecutionOutcome(succeeded, outcome));
        }
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
