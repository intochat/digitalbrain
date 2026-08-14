using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Core.Activities;
using Brain.Core.Capabilities;
using Brain.Core.Endpoints;
using Brain.Core.Modules;
using Brain.Testing.Fakes;
using Xunit;

namespace Brain.Proof.Tests;

#pragma warning disable IDE1006

public sealed class CapabilityAndChildActivityAcceptanceTests
{
    [Fact]
    public async Task capability_requires_verified_activity_context_and_delegated_authority()
    {
        var fixture = new CapabilityAcceptanceFixture();

        await Assert.ThrowsAsync<MissingActivityContextException>(() => fixture.Broker.UseAsync<CapabilityInput, CapabilityResult>(fixture.Descriptor, new CapabilityUseName("use/one"), new CapabilityInput("alpha"), null!, TestContext.Current.CancellationToken));
        var unauthorized = new ActivityContext(fixture.Context.Workspace, fixture.Context.Principal, fixture.Context.Activity, fixture.Context.Correlation, Delegation.Empty);
        await Assert.ThrowsAsync<CapabilityNotDelegatedException>(() => fixture.Broker.UseAsync<CapabilityInput, CapabilityResult>(fixture.Descriptor, new CapabilityUseName("use/two"), new CapabilityInput("alpha"), unauthorized, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task retry_returns_the_durable_capability_result_without_a_second_provider_invocation()
    {
        var fixture = new CapabilityAcceptanceFixture();
        var first = await fixture.Broker.UseAsync<CapabilityInput, CapabilityResult>(fixture.Descriptor, new CapabilityUseName("use/retry"), new CapabilityInput("alpha"), fixture.Context, TestContext.Current.CancellationToken);
        var retry = await fixture.Broker.UseAsync<CapabilityInput, CapabilityResult>(fixture.Descriptor, new CapabilityUseName("use/retry"), new CapabilityInput("different"), fixture.Context, TestContext.Current.CancellationToken);

        Assert.Equal(first, retry);
        Assert.Equal(1, fixture.Provider.CallCount);
    }

    [Fact]
    public async Task child_activity_keeps_its_parent_workspace_principal_and_attenuated_delegation()
    {
        var fixture = new ChildActivityFixture();
        var parent = fixture.CreateParent(new Delegation([fixture.Run.Id, fixture.Correct.Id], []));

        var child = await fixture.Gateway.StartChildAsync<CorrectionInput, CorrectionResult>(parent, fixture.Correct, new CorrectionInput("assessment"), new Delegation([fixture.Correct.Id, new OperationId("other/run@1")], []), TestContext.Current.CancellationToken);
        var state = fixture.Store.Get(child.Activity);

        Assert.Equal(parent, state.ParentActivity);
        Assert.Equal("workspace/child", state.Caller.Workspace.Value);
        Assert.Equal("principal/alice", state.Caller.Principal.Value);
        Assert.Equal([fixture.Correct.Id], state.Delegation.Operations);
    }

    private sealed class CapabilityAcceptanceFixture
    {
        public CapabilityAcceptanceFixture()
        {
            Descriptor = new CapabilityDescriptor(new CapabilityId("proof/classify@1"), new ContractId("proof/classify-input@1"), new ContractId("proof/classify-result@1"), new ModuleId("provider"), new ContractVersion(1));
            var registry = new ModuleRegistry();
            registry.Resolve([new ModuleManifest(new ModuleId("provider"), new ModuleVersion(1, 0, 0), [], [], [], [], [], [], [Descriptor], [])]);
            Provider = new DeterministicCapability();
            Broker = new CapabilityBroker(registry, new AllowedPolicy(), new CapabilityBindingResolver([CapabilityBinding.For<CapabilityInput, CapabilityResult>(Descriptor, async (input, cancellationToken) => new CapabilityResult((await Provider.InvokeAsync(new ProofCapabilityInput(input.Value), cancellationToken)).Classification))]), new CapabilityUseState());
            Context = new ActivityContext(new WorkspaceId("workspace/capability"), new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("capability/one"), new Delegation([], [Descriptor.Id]));
        }
        public CapabilityDescriptor Descriptor { get; }
        public DeterministicCapability Provider { get; }
        public CapabilityBroker Broker { get; }
        public ActivityContext Context { get; }
    }

    private sealed class ChildActivityFixture
    {
        public ChildActivityFixture()
        {
            Run = Operation("proof/run@1");
            Correct = Operation("proof/correct@1");
            var registry = new ModuleRegistry();
            registry.Resolve([new ModuleManifest(new ModuleId("proof"), new ModuleVersion(1, 0, 0), [], [new NeuronRoleDescriptor(Run.EntryRole, NeuronScope.Workspace, Run.Owner)], [Run, Correct], [], [], [], [], [])]);
            Store = new InMemoryActivityStore();
            Gateway = new OperationGateway(registry, new AllowedPolicy(), new EndpointResolver(ManifestValidator.Validate([new ModuleManifest(new ModuleId("proof"), new ModuleVersion(1, 0, 0), [], [new NeuronRoleDescriptor(Run.EntryRole, NeuronScope.Workspace, Run.Owner)], [Run, Correct], [], [], [], [], [])])), new NoopDispatcher(), Store, new ActivityProjectionService(Store), new OperationTypeBindings([OperationTypeBinding.For<ProofInput, ProofResult>(Run, new Canonicalizer<ProofInput>()), OperationTypeBinding.For<CorrectionInput, CorrectionResult>(Correct, new Canonicalizer<CorrectionInput>())]));
        }
        public OperationDescriptor Run { get; }
        public OperationDescriptor Correct { get; }
        public InMemoryActivityStore Store { get; }
        public OperationGateway Gateway { get; }
        public BrainActivityId CreateParent(Delegation delegation)
        {
            var parent = BrainActivityId.New();
            Store.CreateAccepted(new BrainActivityState(parent, Run.Id, new WorkspaceContext(new WorkspaceId("workspace/child"), new PrincipalId("principal/alice"), false), new IdempotencyKey("parent/one"), new CorrelationId("parent/one"), null, Run.TerminalResultContract, delegation, "parent"));
            return parent;
        }
        private static OperationDescriptor Operation(string id)
        {
            var name = id.Contains("correct", StringComparison.Ordinal) ? "correct" : "run";
            return new(new OperationId(id), new ContractId("proof/" + name + "-input@1"), new ContractId("proof/" + name + "-result@1"), new NeuronRoleId("proof.entry"), new ModuleId("proof"), new ContractVersion(1));
        }
    }

    private sealed class AllowedPolicy : IWorkspacePolicyEvaluator
    {
        public PolicyDecision AuthorizeOperation(WorkspaceContext caller, OperationDescriptor operation) => PolicyDecision.Allowed;
        public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request) => PolicyDecision.Allowed;
        public PolicyDecision AuthorizeCapability(ActivityContext context, CapabilityDescriptor capability) => PolicyDecision.Allowed;
    }
    private sealed class NoopDispatcher : IEntryOperationDispatcher { public Task DispatchAsync<T>(EndpointAddress endpoint, OperationInvocation<T> invocation, ActivityContext context, CancellationToken cancellationToken) where T : class => Task.CompletedTask; }
    private sealed class Canonicalizer<T> : IIdempotencyInputCanonicalizer<T> where T : class { public string Canonicalize(T input) => input.GetType().Name; }
    private sealed record CapabilityInput(string Value);
    private sealed record CapabilityResult(string Value);
    private sealed record ProofInput(string Value);
    private sealed record ProofResult;
    private sealed record CorrectionInput(string Value);
    private sealed record CorrectionResult;
}

#pragma warning restore IDE1006
