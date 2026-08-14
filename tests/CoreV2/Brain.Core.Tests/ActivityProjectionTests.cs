using System.Reflection;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Core.Activities;
using Xunit;

namespace Brain.Core.Tests;

public sealed class ActivityProjectionTests
{
    [Fact]
    public async Task OwnerCanObserveOnlyRedactedActivityProjection()
    {
        var store = new InMemoryActivityStore();
        var activity = BrainActivityId.New();
        var caller = new WorkspaceContext(
            new WorkspaceId("workspace/sales"),
            new PrincipalId("principal/alice"),
            isServicePrincipal: false);
        store.CreateAccepted(new BrainActivityState(
            activity,
            new OperationId("proof.run"),
            caller,
            new IdempotencyKey("request/one"),
            new CorrelationId("correlation/one"),
            parentActivity: null,
            new ContractId("proof/run-result@1"),
            Delegation.Empty,
            inputFingerprint: "fingerprint"));
        var service = new ActivityProjectionService(store);

        var view = await service.ObserveAsync(activity, caller, TestContext.Current.CancellationToken);

        Assert.Equal(ActivityStatus.Accepted, view.Status);
        Assert.DoesNotContain(typeof(ActivityView).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.Name is "Caller" or "Input" or "Journal" or "Entity" or "ProviderResponse");
        Assert.DoesNotContain(typeof(BrainActivityState).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.Name.Contains("Journal", StringComparison.Ordinal) || property.Name.Contains("Entity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DifferentPrincipalCannotObserveAnActivityProjection()
    {
        var store = new InMemoryActivityStore();
        var activity = BrainActivityId.New();
        var owner = new WorkspaceContext(
            new WorkspaceId("workspace/sales"),
            new PrincipalId("principal/alice"),
            isServicePrincipal: false);
        store.CreateAccepted(new BrainActivityState(
            activity,
            new OperationId("proof.run"),
            owner,
            new IdempotencyKey("request/one"),
            new CorrelationId("correlation/one"),
            parentActivity: null,
            new ContractId("proof/run-result@1"),
            Delegation.Empty,
            inputFingerprint: "fingerprint"));
        var service = new ActivityProjectionService(store);
        var other = new WorkspaceContext(
            owner.Workspace,
            new PrincipalId("principal/bob"),
            isServicePrincipal: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ObserveAsync(activity, other, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GrainSupportsTheProofLifecycleWithoutInventingOtherWorkflowTransitions()
    {
        var store = new InMemoryActivityStore();
        var activity = BrainActivityId.New();
        var state = new BrainActivityState(
            activity,
            new OperationId("proof.run"),
            new WorkspaceContext(new WorkspaceId("workspace/sales"), new PrincipalId("principal/alice"), false),
            new IdempotencyKey("request/one"),
            new CorrelationId("correlation/one"),
            parentActivity: null,
            new ContractId("proof/run-result@1"),
            Delegation.Empty,
            inputFingerprint: "fingerprint");
        var grain = new BrainActivityGrain(store, activity);

        grain.Accept(state);
        grain.MarkRunning();
        grain.Complete(new ActivityResultReference(
            new ContractId("proof/run-result@1"),
            new ActivityPayloadReference("result/one")));

        Assert.Equal(ActivityStatus.Completed, store.Get(activity).Status);
        Assert.Equal(
            [
                ActivityStatus.Accepted,
                ActivityStatus.Running,
                ActivityStatus.AwaitingConfirmation,
                ActivityStatus.Completed,
                ActivityStatus.Refused,
                ActivityStatus.Failed,
                ActivityStatus.Cancelled,
            ],
            Enum.GetValues<ActivityStatus>());
    }
}
