using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Xunit;
using DigitalBrain.Behaviors.Runtime;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorUserActionContinuation(BehaviorHostedExecutionFixture fixture)
{
    [Fact(
        Timeout = 90_000,
        DisplayName =
            "Hosted Accept that needs user action parks Task Waiting; completer Continue restages hosted run on same attempt")]
    public async Task HostedUserActionParksAndContinueRestagesSameAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var worker = brain.Neuron<IWorker>("hosted-user-action-worker");
        var task = brain.Neuron<ITask>("hosted-user-action-task");
        var activation = Activation(new ProtectedPayloadReference(
            Guid.Parse("bbbbbbbb-2222-3333-4444-cccccccccccc"),
            brain.Clock.UtcNow.AddHours(1)));
        var goal = Goal(activation);
        AttemptId? parkedAttempt = null;
        IssuedUserAction? issued = null;
        NeuronId? completer = null;

        fixture.Executor.Reset(succeeded: false, outcome: BehaviorExecutionCodes.UserActionRequired);
        fixture.Executor.OnHardened = async (request, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (parkedAttempt is null)
            {
                parkedAttempt = request.Attempt;
                var actionEpoch = Guid.NewGuid();
                // Fixture completer is the owner session so Client.SendAsync is an authorized caller.
                // Production StageDispatch uses UserActionCompletionBridge (covered by Integrations.Tests).
                completer = ISessionNeuron.ForOwner(request.Task.Owner);
                var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
                var custody = new GrainUserActionCustody(payloads, new FixedTimeProvider(request.UtcNow));
                issued = await ModuleUserActionBoundary.IssueAsync(
                    custody,
                    request.Task.Owner,
                    request.Task,
                    request.Attempt,
                    moduleNeuron: new NeuronId("google.gmail", request.Task.Owner, "gmail"),
                    moduleId: "google.gmail",
                    displayText: "Connect Gmail to continue",
                    signInUrl: new Uri("https://accounts.google.com/o/oauth2/v2/auth?state=secret"),
                    state: "secret-state",
                    parkRevision: 0,
                    lifetime: TimeSpan.FromHours(1),
                    completer.Value,
                    actionEpoch,
                    token);
                throw new BehaviorUserActionRequiredException(issued.Requirement);
            }

            return new BehaviorExecutionOutcome(true, BehaviorExecutionCodes.Succeeded);
        };

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var required = await task.Incoming.NextAsync<UserActionRequired>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, required.Synapse.Attempt);
        Assert.Equal("google.gmail", required.Synapse.ModuleId);
        Assert.NotNull(issued);
        Assert.NotNull(completer);
        Assert.Equal(issued.Requirement.ActionReference, required.Synapse.ActionReference);
        Assert.Equal(issued.Requirement.ActionEpoch, required.Synapse.ActionEpoch);
        Assert.Equal(issued.Requirement.ParkRevision, required.Synapse.ParkRevision);
        Assert.Equal(completer, required.Synapse.Completer);
        Assert.True(
            required.Synapse.ActionReference.ExpiresAt is { } referenceExpiry
            && referenceExpiry > brain.Clock.UtcNow,
            $"ActionReference.ExpiresAt must be future on silo clock (got {required.Synapse.ActionReference.ExpiresAt}, silo {brain.Clock.UtcNow}).");
        Assert.True(
            required.Synapse.ExpiresAt > brain.Clock.UtcNow,
            $"UserActionRequired.ExpiresAt must be future on silo clock (got {required.Synapse.ExpiresAt}, silo {brain.Clock.UtcNow}).");

        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        Assert.Equal(started.ActiveAttempt, waiting.ActiveAttempt);
        Assert.Equal(1, waiting.AttemptCount);
        var blocker = Assert.IsType<UserActionPending>(waiting.Blocker);
        Assert.Equal(1, fixture.Executor.HardenedCalls);
        Assert.Equal(required.Synapse.ActionReference, blocker.ActionReference);
        Assert.Equal(required.Synapse.ActionEpoch, blocker.ActionEpoch);
        Assert.Equal(completer, blocker.Completer);

        var surface = ModuleUserActionBoundary.SerializeSafeSurface(required.Synapse);
        Assert.False(ModuleUserActionBoundary.SurfaceContainsSecretFragments(surface));
        Assert.DoesNotContain("https://accounts.google.com", surface, StringComparison.Ordinal);

        await brain.Client.SendAsync(
            task.Id,
            new CompleteUserAction(
                CommandId.New(),
                required.Synapse.ActionReference,
                required.Synapse.ActionEpoch,
                required.Synapse.ParkRevision),
            cancellationToken);

        var completed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(started.ActiveAttempt, completed.ActiveAttempt);
        Assert.Null(completed.Blocker);

        var succeeded = await task.Incoming.NextAsync<AttemptSucceeded>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, succeeded.Synapse.Attempt);
        Assert.Equal(completed.Revision, succeeded.Synapse.Revision);

        var terminal = await WaitForStateAsync(task, TaskState.Succeeded, cancellationToken);
        Assert.Equal(2, fixture.Executor.HardenedCalls);
        Assert.Equal(started.ActiveAttempt, parkedAttempt);
        Assert.IsType<BehaviorTaskResult>(terminal.Result);
        Assert.Equal(1, terminal.AttemptCount);
    }

    [Fact(DisplayName =
        "Bare behavior-user-action-required outcome without surface fails closed and does not park Waiting")]
    public async Task BareUserActionCodeWithoutSurfaceFailsClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var worker = brain.Neuron<IWorker>("bare-user-action-worker");
        var task = brain.Neuron<ITask>("bare-user-action-task");
        var activation = Activation(new ProtectedPayloadReference(
            Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"),
            brain.Clock.UtcNow.AddHours(1)));
        var goal = Goal(activation);

        fixture.Executor.Reset(succeeded: false, outcome: BehaviorExecutionCodes.UserActionRequired);
        fixture.Executor.OnHardened = (_, _) =>
            ValueTask.FromResult(
                new BehaviorExecutionOutcome(false, BehaviorExecutionCodes.UserActionRequired));

        _ = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation));

        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var failed = await task.Incoming.NextAsync<AttemptFailed>(cancellationToken);
        Assert.Equal(BehaviorExecutionCodes.Exception, ((BehaviorTaskFailure)failed.Synapse.Failure).Reason);

        var terminal = await WaitForStateAsync(task, TaskState.Failed, cancellationToken);
        Assert.Null(terminal.Blocker);
        Assert.NotEqual(TaskState.Waiting, terminal.State);
    }

    [Fact(DisplayName =
        "ModuleUserActionBoundary stores secrets in protected material and exposes only safe UserActionRequired fields")]
    public void BoundaryMapsSecretsIntoProtectedMaterialOnly()
    {
        var task = NeuronId.For<ITask>(new OwnerId("owner-a"), "boundary-task");
        var attempt = new AttemptId(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        var module = new NeuronId("google.gmail", task.Owner, "gmail");
        var completer = UserActionCompletionBridge.For(task.Owner, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var actionReference = new ProtectedPayloadReference(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            DateTimeOffset.Parse("2026-07-31T15:00:00Z", CultureInfo.InvariantCulture));

        var protectedBytes = ModuleUserActionBoundary.ProtectActionMaterial(
            signInUrl: new Uri("https://accounts.google.com/o/oauth2/v2/auth?state=secret-state"),
            state: "secret-state",
            authorizationCode: "auth-code-must-not-surface");
        Assert.NotEmpty(protectedBytes);
        Assert.Contains("secret-state"u8.ToArray(), protectedBytes.AsSpan());

        var action = ModuleUserActionBoundary.Create(
            task,
            attempt,
            module,
            "google.gmail",
            "Connect Gmail",
            actionReference,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            parkRevision: 0,
            DateTimeOffset.Parse("2026-07-31T15:00:00Z", CultureInfo.InvariantCulture),
            completer);

        var surface = ModuleUserActionBoundary.SerializeSafeSurface(action);
        Assert.False(ModuleUserActionBoundary.SurfaceContainsSecretFragments(surface));
        Assert.DoesNotContain("secret-state", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("auth-code-must-not-surface", surface, StringComparison.Ordinal);
        Assert.Contains("google.gmail", surface, StringComparison.Ordinal);
        Assert.Contains("Connect Gmail", surface, StringComparison.Ordinal);
        Assert.Contains(completer.Name, surface, StringComparison.Ordinal);
    }

    private static BehaviorTaskActivation Activation(ProtectedPayloadReference triggerRef)
        => new(
            new BehaviorId(BehaviorsFixture.SampleBehavior),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "case.SampleTrigger",
            protectedPayload: triggerRef,
            triggerTypeName: "SampleTrigger",
            capabilities: []);

    private static BehaviorActivationGoal Goal(BehaviorTaskActivation activation)
        => new(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

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
        throw new TimeoutException($"Timed out waiting for {expected}. Final state: {final.State}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
