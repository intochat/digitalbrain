using System.Globalization;
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
    public void RuntimeIngressDoesNotDiscoverInputClrShape()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
        var ingress = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CoreV2",
            "Brain.Core",
            "Activities",
            "OperationGateway.cs"));

        Assert.DoesNotContain("System.Reflection", ingress, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperties", ingress, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyQualifiedName", ingress, StringComparison.Ordinal);
        Assert.DoesNotContain("GetInterfaces", ingress, StringComparison.Ordinal);
    }

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
    public async Task ReusedKeyWithDifferentArrayContentsIsRefusedWithoutAnotherDispatch()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/array");

        await fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
            fixture.Collect,
            new CollectionInput(["alpha"], new Dictionary<string, int> { ["score"] = 1 }),
            caller,
            key,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
                fixture.Collect,
                new CollectionInput(["beta"], new Dictionary<string, int> { ["score"] = 1 }),
                caller,
                key,
                TestContext.Current.CancellationToken));

        Assert.Single(fixture.Store.Activities);
        Assert.Single(fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task ReusedKeyWithReversedMultiItemArrayIsRefusedWithoutAnotherDispatch()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/array-order");

        await fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
            fixture.Collect,
            new CollectionInput(["alpha", "beta"], new Dictionary<string, int> { ["score"] = 1 }),
            caller,
            key,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
                fixture.Collect,
                new CollectionInput(["beta", "alpha"], new Dictionary<string, int> { ["score"] = 1 }),
                caller,
                key,
                TestContext.Current.CancellationToken));

        Assert.Single(fixture.Store.Activities);
        Assert.Single(fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task ReusedKeyWithReversedListThroughReadOnlyListIsRefusedWithoutAnotherDispatch()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/list-order");
        IReadOnlyList<string> firstItems = new List<string> { "alpha", "beta" };
        IReadOnlyList<string> reversedItems = new List<string> { "beta", "alpha" };

        await fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
            fixture.Collect,
            new CollectionInput(firstItems, new Dictionary<string, int> { ["score"] = 1 }),
            caller,
            key,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
                fixture.Collect,
                new CollectionInput(reversedItems, new Dictionary<string, int> { ["score"] = 1 }),
                caller,
                key,
                TestContext.Current.CancellationToken));

        Assert.Single(fixture.Store.Activities);
        Assert.Single(fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task ReusedKeyWithDifferentDictionaryValueIsRefusedWithoutAnotherDispatch()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/dictionary");

        await fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
            fixture.Collect,
            new CollectionInput(["alpha"], new Dictionary<string, int> { ["score"] = 1 }),
            caller,
            key,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
                fixture.Collect,
                new CollectionInput(["alpha"], new Dictionary<string, int> { ["score"] = 2 }),
                caller,
                key,
                TestContext.Current.CancellationToken));

        Assert.Single(fixture.Store.Activities);
        Assert.Single(fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task ReusedKeyWithReverseInsertedEquivalentDictionaryReturnsTheSameActivity()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/dictionary-order");

        var first = await fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
            fixture.Collect,
            new CollectionInput(
                ["alpha"],
                new Dictionary<string, int> { ["alpha"] = 1, ["beta"] = 2 }),
            caller,
            key,
            TestContext.Current.CancellationToken);
        var retry = await fixture.Gateway.InvokeAsync<CollectionInput, ProofResult>(
            fixture.Collect,
            new CollectionInput(
                ["alpha"],
                new Dictionary<string, int> { ["beta"] = 2, ["alpha"] = 1 }),
            caller,
            key,
            TestContext.Current.CancellationToken);

        Assert.Equal(first.Activity, retry.Activity);
        Assert.Single(fixture.Store.Activities);
        Assert.Single(fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task ReusedKeyWithDifferentQueueOrderIsRefusedWithoutAnotherDispatch()
    {
        var fixture = GatewayFixture.Allowed();
        var caller = Caller("workspace/sales", "principal/alice");
        var key = new IdempotencyKey("request/queue");

        await fixture.Gateway.InvokeAsync<QueueInput, ProofResult>(
            fixture.Sequence,
            new QueueInput(new Queue<string>(["alpha", "beta"])),
            caller,
            key,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            fixture.Gateway.InvokeAsync<QueueInput, ProofResult>(
                fixture.Sequence,
                new QueueInput(new Queue<string>(["beta", "alpha"])),
                caller,
                key,
                TestContext.Current.CancellationToken));

        Assert.Single(fixture.Store.Activities);
        Assert.Single(fixture.Dispatcher.Calls);
    }

    [Fact]
    public async Task RegisteredDescriptorRejectsIncompatibleGenericInputAndResultBeforeActivityCreation()
    {
        var fixture = GatewayFixture.Allowed();

        await Assert.ThrowsAsync<OperationTypeMismatchException>(() =>
            fixture.Gateway.InvokeAsync<CorrectionInput, CorrectionResult>(
                fixture.Run,
                new CorrectionInput("assessment"),
                Caller("workspace/sales", "principal/alice"),
                new IdempotencyKey("request/type-mismatch"),
                TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Store.Activities);
        Assert.Empty(fixture.Dispatcher.Calls);
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
    public async Task ConfirmationRequirementTransitionsToAwaitingConfirmationWithoutDispatch()
    {
        var fixture = GatewayFixture.ConfirmationRequired();
        var caller = Caller("workspace/sales", "principal/alice");

        var accepted = await fixture.Gateway.InvokeAsync<ProofInput, ProofResult>(
            fixture.Run,
            new ProofInput("alpha"),
            caller,
            new IdempotencyKey("request/confirmation"),
            TestContext.Current.CancellationToken);

        var view = await fixture.Gateway.ObserveAsync(
            accepted.Activity,
            caller,
            TestContext.Current.CancellationToken);

        Assert.Equal(ActivityStatus.AwaitingConfirmation, view.Status);
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

    private sealed record CollectionInput(
        IReadOnlyList<string> Items,
        IReadOnlyDictionary<string, int> Values);

    private sealed record QueueInput(IEnumerable<string> Items);

    private sealed class DelegateCanonicalizer<TInput>(Func<TInput, string> canonicalize)
        : IIdempotencyInputCanonicalizer<TInput>
        where TInput : class
    {
        public string Canonicalize(TInput input) => canonicalize(input);
    }

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
            Collect = new OperationDescriptor(
                new OperationId("proof.collect"),
                new ContractId("proof/collect-input@1"),
                new ContractId("proof/collect-result@1"),
                new NeuronRoleId("proof.entry"),
                new ModuleId("proof"),
                new ContractVersion(1));
            Sequence = new OperationDescriptor(
                new OperationId("proof.sequence"),
                new ContractId("proof/sequence-input@1"),
                new ContractId("proof/sequence-result@1"),
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
                    [Run, Correct, Collect, Sequence],
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
                new ActivityProjectionService(Store),
                new OperationTypeBindings(
                [
                    OperationTypeBinding.For<ProofInput, ProofResult>(
                        Run,
                        Canonicalizer<ProofInput>(input => Token("proof", input.Value))),
                    OperationTypeBinding.For<CorrectionInput, CorrectionResult>(
                        Correct,
                        Canonicalizer<CorrectionInput>(input => Token("correction", input.Value))),
                    OperationTypeBinding.For<CollectionInput, ProofResult>(
                        Collect,
                        Canonicalizer<CollectionInput>(input => Token(
                            "collection",
                            Token("items", SequenceMaterial(input.Items))
                            + Token("values", DictionaryMaterial(input.Values))))),
                    OperationTypeBinding.For<QueueInput, ProofResult>(
                        Sequence,
                        Canonicalizer<QueueInput>(input => Token("queue", SequenceMaterial(input.Items)))),
                ]));
        }

        public OperationDescriptor Run { get; }

        public OperationDescriptor Correct { get; }

        public OperationDescriptor Collect { get; }

        public OperationDescriptor Sequence { get; }

        public InMemoryActivityStore Store { get; }

        public RecordingDispatcher Dispatcher { get; }

        public OperationGateway Gateway { get; }

        public static GatewayFixture Allowed() => new(PolicyDecision.Allowed);

        public static GatewayFixture Refused() => new(PolicyDecision.Refused);

        public static GatewayFixture ConfirmationRequired() => new(PolicyDecision.ConfirmationRequired);

        private static IIdempotencyInputCanonicalizer<TInput> Canonicalizer<TInput>(Func<TInput, string> canonicalize)
            where TInput : class
            => new DelegateCanonicalizer<TInput>(canonicalize);

        private static string SequenceMaterial(IEnumerable<string> values)
            => string.Concat(values.Select(value => Token("item", value)));

        private static string DictionaryMaterial(IReadOnlyDictionary<string, int> values)
            => string.Concat(values
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => Token(
                    "entry",
                    Token("key", pair.Key)
                    + Token("value", pair.Value.ToString(CultureInfo.InvariantCulture)))));

        private static string Token(string kind, string value)
            => $"{kind}:{value.Length.ToString(CultureInfo.InvariantCulture)}:{value};";
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
