using Brain.Abstractions.Activities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Core.Activities;
using Brain.Core.Endpoints;
using Brain.Core.Modules;
using Xunit;

namespace Brain.Core.Tests;

public sealed class OperationGatewayTests
{
    [Fact]
    public async Task SameWorkspacePrincipalAndKeyReturnTheSameActivity()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/42");

        var first = await fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
            fixture.Run,
            new ProofInput("alpha"),
            caller,
            key,
            TestContext.Current.CancellationToken);
        var retry = await fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
            fixture.Run,
            new ProofInput("alpha"),
            caller,
            key,
            TestContext.Current.CancellationToken);

        Assert.Equal(first.Activity, retry.Activity);
        Assert.Single(fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task ReusedKeyForAnotherOperationIsRefused()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/42");

        await fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
            fixture.Run,
            new ProofInput("alpha"),
            caller,
            key,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            fixture.Gateway.InvokeAsync<CorrectionInput, CorrectionResult>(
                fixture.Correct,
                new CorrectionInput("assessment"),
                caller,
                key,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReusedKeyForTheSameOperationWithDifferentInputIsRefused()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/42");

        await fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
            fixture.Run,
            new ProofInput("alpha"),
            caller,
            key,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
                fixture.Run,
                new ProofInput("beta"),
                caller,
                key,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PolicyRefusalCreatesAndPersistsARefusedActivity()
    {
        var fixture = GatewayFixture.Refused();
        var caller = Caller("workspace/sales", "principal/alice");

        var accepted = await fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
            fixture.Run,
            new ProofInput("alpha"),
            caller,
            new IdempotencyKey("request/refused"),
            TestContext.Current.CancellationToken);

        var view = await fixture.Gateway.ObserveAsync(
            accepted.Activity,
            caller,
            TestContext.Current.CancellationToken);

        Assert.Equal(ActivityStatus.Refused, view.Status);
        Assert.Equal("policy-refused", view.Problem!.Code);
        Assert.Empty(fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task UnregisteredOperationFailsBeforeAnyActivityIsCreated()
    {
        var fixture = GatewayFixture.Allowed();
        var unregistered = Operation("proof.unregistered", "proof.unregistered-entry");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
                unregistered,
                new ProofInput("alpha"),
                Caller("workspace/sales", "principal/alice"),
                new IdempotencyKey("request/unregistered"),
                TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Store.Activities);
    }

    [Fact]
    public async Task DescriptorWhoseRegisteredOperationContractDoesNotMatchFailsBeforeAnyActivityIsCreated()
    {
        var fixture = GatewayFixture.Allowed();
        var mismatched = new OperationDescriptor(
            fixture.Run.Id,
            new ContractId("proof/other-input@1"),
            fixture.Run.TerminalResultContract,
            fixture.Run.EntryRole,
            fixture.Run.Owner,
            fixture.Run.Version);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
                mismatched,
                new ProofInput("alpha"),
                Caller("workspace/sales", "principal/alice"),
                new IdempotencyKey("request/mismatched"),
                TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Store.Activities);
    }

    [Fact]
    public async Task AcceptedActivityIsPersistedBeforeDirectEntryDispatchWithoutGraphOrEventRouting()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");

        await fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
            fixture.Run,
            new ProofInput("alpha"),
            caller,
            new IdempotencyKey("request/direct"),
            TestContext.Current.CancellationToken);

        var dispatch = Assert.Single(fixture.Dispatcher.Calls);
        Assert.Equal(fixture.Run.EntryRole, dispatch.Endpoint.Role);
        Assert.Equal("alpha", Assert.IsType<ProofInput>(dispatch.Input).Value);
        Assert.True(fixture.Dispatcher.ActivityExistedAtDispatch);
        Assert.DoesNotContain(typeof(OperationGateway).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            field => field.FieldType.Name.Contains("Graph", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChildActivityRecordsParentAndReceivesOnlyAttenuatedDelegation()
    {
        var fixture = GatewayFixture.Allowed();
        var parent = BrainActivityId.New();
        fixture.Store.CreateAccepted(new BrainActivityState(
            parent,
            fixture.Run.Id,
            Caller("workspace/sales", "principal/alice"),
            new IdempotencyKey("parent/key"),
            new CorrelationId("correlation/parent"),
            parentActivity: null,
            fixture.Run.TerminalResultContract,
            new Delegation([fixture.Run.Id, fixture.Correct.Id], []),
            inputFingerprint: "parent"));

        var child = await fixture.Gateway.StartChildAsync<CorrectionInput, CorrectionResult>(
            parent,
            fixture.Correct,
            new CorrectionInput("assessment"),
            new Delegation([fixture.Correct.Id, new OperationId("proof.not-allowed")], []),
            TestContext.Current.CancellationToken);

        var state = fixture.Store.Get(child.Activity);
        Assert.Equal(parent, state.ParentActivity);
        Assert.Equal([fixture.Correct.Id], state.Delegation.Operations.OrderBy(static operation => operation.Value));
        Assert.Equal("child/" + parent + "/" + fixture.Correct.Id, state.IdempotencyKey.Value);
    }

    private static WorkspaceContext Caller(string workspace, string principal)
        => new(new WorkspaceId(workspace), new PrincipalId(principal), isServicePrincipal: false);

    private static OperationDescriptor Operation(string operation, string entryRole)
        => new(
            new OperationId(operation),
            new ContractId("proof/run-input@1"),
            new ContractId("proof/run-result@1"),
            new NeuronRoleId(entryRole),
            new ModuleId("proof"),
            new ContractVersion(1));

    private sealed record ProofInput(string Value);

    private sealed record ProofResult;

    private sealed record CorrectionInput(string Value);

    private sealed record CorrectionResult;

    private sealed class GatewayFixture
    {
        private GatewayFixture(PolicyDecision decision)
        {
            Run = Operation("proof.run", "proof.entry");
            Correct = new OperationDescriptor(
                new OperationId("proof.correct"),
                new ContractId("proof/correct-input@1"),
                new ContractId("proof/correct-result@1"),
                new NeuronRoleId("proof.entry"),
                new ModuleId("proof"),
                new ContractVersion(1));
            var registry = new ModuleRegistry();
            registry.Resolve(
            [
                new ModuleManifest(
                    new ModuleId("proof"),
                    new ModuleVersion(1, 0, 0),
                    [],
                    [new NeuronRoleDescriptor(Run.EntryRole, NeuronScope.Workspace, Run.Owner)],
                    [Run, Correct],
                    [],
                    [],
                    [],
                    [],
                    []),
            ]);

            Store = new InMemoryActivityStore();
            Dispatcher = new RecordingDispatcher(Store);
            Gateway = new OperationGateway(
                registry,
                new FixedPolicyEvaluator(decision),
                new TestEndpointResolver(),
                Dispatcher,
                Store,
                new ActivityProjectionService(Store));
        }

        public OperationDescriptor Run { get; }

        public OperationDescriptor Correct { get; }

        public InMemoryActivityStore Store { get; }

        public RecordingDispatcher Dispatcher { get; }

        public OperationGateway Gateway { get; }

        public static GatewayFixture Allowed() => new(PolicyDecision.Allowed);

        public static GatewayFixture Refused() => new(PolicyDecision.Refused);
    }

    private sealed class FixedPolicyEvaluator(PolicyDecision decision) : IWorkspacePolicyEvaluator
    {
        public PolicyDecision AuthorizeOperation(WorkspaceContext caller, OperationDescriptor operation) => decision;

        public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request)
            => PolicyDecision.Refused;
    }

    private sealed class TestEndpointResolver : IEndpointResolver
    {
        public EndpointAddress Resolve(NeuronRoleDescriptor role, WorkspaceContext context)
            => new(context.Workspace, role.Owner, role.Id, "workspace");
    }

    private sealed class RecordingDispatcher(InMemoryActivityStore store) : IEntryOperationDispatcher
    {
        public List<DispatchCall> Calls { get; } = [];

        public bool ActivityExistedAtDispatch { get; private set; }

        public Task DispatchAsync<TInput>(
            EndpointAddress endpoint,
            OperationInvocation<TInput> invocation,
            ActivityContext context,
            CancellationToken cancellationToken)
            where TInput : class
        {
            ActivityExistedAtDispatch = store.Activities.ContainsKey(context.Activity);
            Calls.Add(new DispatchCall(endpoint, invocation.Input));
            return Task.CompletedTask;
        }
    }

    private sealed record DispatchCall(EndpointAddress Endpoint, object Input);
}
