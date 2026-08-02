using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DigitalBrain.Behaviors.Runtime;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorDispatchAuthorityTests(BehaviorDispatchFixture fixture)
{
    [Fact(
        Timeout = 90_000,
        DisplayName =
            "GrainBehaviorCapabilityDispatchAccess refuses wrong attempt, missing task, and non-running activation with zero probe deliveries")]
    public async Task AccessRefusesWrongAttemptTaskAndNonRunningWithZeroDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, attempt, edge, requestRef, requestText) = await StartAndStoreAsync(
            brain,
            "access-refuse",
            cancellationToken);
        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var access = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);

        var wrongAttempt = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.DispatchAsync(
                task.Id.Owner,
                task.Id,
                new AttemptId(Guid.Parse("44444444444444444444444444444444")),
                edge,
                requestRef,
                cancellationToken));
        Assert.Equal("attempt-mismatch", wrongAttempt.Message);
        Assert.Equal(0, DispatchHarness.CountFor(requestText));

        var missingTask = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.DispatchAsync(
                task.Id.Owner,
                NeuronId.For<ITask>(task.Id.Owner, "missing-dispatch-task"),
                attempt,
                edge,
                requestRef,
                cancellationToken));
        Assert.Equal("task-not-started", missingTask.Message);
        Assert.Equal(0, DispatchHarness.CountFor(requestText));

        var cancelSnapshot = await task.Reference.Cancel(
            new CancelTask(CommandId.New(), (await task.Reference.Read()).Revision));
        Assert.True(cancelSnapshot.State is TaskState.Cancelling or TaskState.Cancelled);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        TaskSnapshot cancelled;
        do
        {
            cancelled = await task.Reference.Read();
            if (cancelled.State == TaskState.Cancelled)
            {
                break;
            }

            await Task.Delay(50, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Equal(TaskState.Cancelled, cancelled.State);
        var notActive = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                edge,
                requestRef,
                cancellationToken));
        Assert.Equal("attempt-mismatch", notActive.Message);
        Assert.Equal(0, DispatchHarness.CountFor(requestText));
        _ = worker;
    }

    [Fact(
        Timeout = 90_000,
        DisplayName =
            "Worker StageDispatch revalidates active attempt and bound Worker; wrong attempt or wrong Worker yields zero probe deliveries")]
    public async Task StageDispatchRevalidatesAttemptAndWorkerWithZeroDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (worker, task, attempt, edge, requestRef, requestText) = await StartAndStoreAsync(
            brain,
            "stage-refuse",
            cancellationToken);

        var boundWorker = brain.Cluster.Client.GetGrain<IBehaviorWorkerBroker>(worker.Id.ToGrainId());
        var wrongAttempt = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await boundWorker.StageDispatch(
                task.Id,
                new AttemptId(Guid.Parse("55555555555555555555555555555555")),
                edge,
                requestRef,
                cancellationToken));
        Assert.Contains("attempt-mismatch", RootMessage(wrongAttempt), StringComparison.Ordinal);
        Assert.Equal(0, DispatchHarness.CountFor(requestText));

        var foreignWorkerNeuron = brain.Neuron<IWorker>("stage-refuse-foreign-worker");
        var foreignBroker = brain.Cluster.Client.GetGrain<IBehaviorWorkerBroker>(
            foreignWorkerNeuron.Id.ToGrainId());
        var wrongWorker = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await foreignBroker.StageDispatch(
                task.Id,
                attempt,
                edge,
                requestRef,
                cancellationToken));
        Assert.Contains("worker-mismatch", RootMessage(wrongWorker), StringComparison.Ordinal);
        Assert.Equal(0, DispatchHarness.CountFor(requestText));
    }

    [Fact(
        Timeout = 90_000,
        DisplayName =
            "catalog refusals pin exact stable reasons for unknown target, request version drift, response version drift, and unknown method-shaped request synapse")]
    public async Task CatalogRefusalsPinExactStableReasonsWithZeroDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var (_, task, attempt, edge, requestRef, requestText) = await StartAndStoreAsync(
            brain,
            "catalog-refuse",
            cancellationToken);
        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var access = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);

        var unknownTarget = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                new BehaviorCapabilityEdge(
                    new NeuronId("unknown.neuron", task.Id.Owner, "nope"),
                    edge.RequestSynapseId,
                    1,
                    edge.ResponseSynapseId,
                    1),
                requestRef,
                cancellationToken));
        Assert.Equal("unknown-target-neuron", unknownTarget.Message);

        var requestDrift = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                new BehaviorCapabilityEdge(
                    edge.Target,
                    edge.RequestSynapseId,
                    2,
                    edge.ResponseSynapseId,
                    1),
                requestRef,
                cancellationToken));
        Assert.Equal("incompatible-request-version", requestDrift.Message);

        var responseDrift = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                new BehaviorCapabilityEdge(
                    edge.Target,
                    edge.RequestSynapseId,
                    1,
                    edge.ResponseSynapseId,
                    2),
                requestRef,
                cancellationToken));
        Assert.Equal("incompatible-response-version", responseDrift.Message);

        var methodShaped = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await access.DispatchAsync(
                task.Id.Owner,
                task.Id,
                attempt,
                new BehaviorCapabilityEdge(
                    edge.Target,
                    "ReadMessage",
                    1,
                    edge.ResponseSynapseId,
                    1),
                requestRef,
                cancellationToken));
        Assert.Equal("unknown-request-synapse", methodShaped.Message);

        Assert.Equal(0, DispatchHarness.CountFor(requestText));
    }

    private static async Task<(
        TestNeuron<IWorker> Worker,
        TestNeuron<ITask> Task,
        AttemptId Attempt,
        BehaviorCapabilityEdge Edge,
        ProtectedPayloadReference RequestRef,
        string RequestText)> StartAndStoreAsync(
        TestBrain brain,
        string name,
        CancellationToken cancellationToken)
    {
        var worker = brain.Neuron<IWorker>($"{name}-worker");
        var task = brain.Neuron<ITask>($"{name}-task");
        var probe = brain.Neuron<IDispatchProbe>($"{name}-probe");
        var requestText = $"{name}-{Guid.NewGuid():N}";
        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: name,
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("66666666-6666-6666-6666-666666666666")),
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

        await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation))
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        TaskSnapshot snapshot;
        do
        {
            snapshot = await task.Reference.Read();
            if (snapshot.State == TaskState.Running && snapshot.ActiveAttempt is not null)
            {
                break;
            }

            await Task.Delay(25, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Equal(TaskState.Running, snapshot.State);
        Assert.NotNull(snapshot.ActiveAttempt);
        var attempt = snapshot.ActiveAttempt!.Value;
        var edge = new BehaviorCapabilityEdge(
            new NeuronId(DispatchHarness.NeuronContractId, task.Id.Owner, probe.Id.Name),
            DispatchHarness.RequestContractId,
            1,
            DispatchHarness.ResponseContractId,
            1);
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var requestRef = await payloads.StoreAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            DispatchHarness.SerializeRequest(requestText),
            cancellationToken);
        return (worker, task, attempt, edge, requestRef, requestText);
    }

    private static string RootMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}
