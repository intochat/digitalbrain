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
using Brain.Modules.Proof.Contracts;
using Brain.Testing;
using Xunit;

namespace Brain.Proof.Tests;

#pragma warning disable IDE1006

public sealed class RetryAndPolicyAcceptanceTests
{
    [Fact]
    public async Task same_key_retries_return_the_same_activity_and_key_reuse_for_another_operation_is_refused()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/retry", "principal/alice");
        var key = new IdempotencyKey("retry/same");
        var first = await host.Operations.InvokeAsync<ProofInput, ProofResult>(ProofContracts.Run, new ProofInput("alpha"), caller, key, TestContext.Current.CancellationToken);
        var retry = await host.Operations.InvokeAsync<ProofInput, ProofResult>(ProofContracts.Run, new ProofInput("alpha"), caller, key, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => host.Operations.InvokeAsync<CorrectionInput, CorrectionResult>(ProofContracts.Correct, new CorrectionInput("assessment"), caller, key, TestContext.Current.CancellationToken));
        Assert.Equal(first.Activity, retry.Activity);
    }

    [Fact]
    public async Task policy_refusal_settles_an_activity_without_any_entry_or_graph_delivery()
    {
        var fixture = new RefusedGatewayFixture();
        var caller = new WorkspaceContext(new WorkspaceId("workspace/refusal"), new PrincipalId("principal/alice"), false);

        var accepted = await fixture.Gateway.InvokeAsync<RefusedInput, RefusedResult>(fixture.Operation, new RefusedInput("alpha"), caller, new IdempotencyKey("refusal/one"), TestContext.Current.CancellationToken);
        var view = await fixture.Gateway.ObserveAsync(accepted.Activity, caller, TestContext.Current.CancellationToken);

        Assert.Equal(ActivityStatus.Refused, view.Status);
        Assert.Equal("policy-refused", view.Problem!.Code);
        Assert.Equal(0, fixture.Dispatches);
    }

    [Fact]
    public async Task unrelated_idempotency_keys_run_concurrently_in_one_workspace()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/concurrent", "principal/alice");
        var accepted = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => host.Operations.InvokeAsync<ProofInput, ProofResult>(ProofContracts.Run, new ProofInput("value-" + index), caller, new IdempotencyKey("concurrent/" + index), TestContext.Current.CancellationToken)));

        Assert.Equal(8, accepted.Select(item => item.Activity).Distinct().Count());
        foreach (var item in accepted)
        {
            var view = await host.Operations.ObserveAsync(item.Activity, caller, TestContext.Current.CancellationToken);
            Assert.Equal(ActivityStatus.Completed, view.Status);
        }
    }

    private sealed class RefusedGatewayFixture
    {
        public RefusedGatewayFixture()
        {
            Operation = new OperationDescriptor(new OperationId("refused/run@1"), new ContractId("refused/input@1"), new ContractId("refused/result@1"), new NeuronRoleId("refused.entry"), new ModuleId("refused"), new ContractVersion(1));
            var modules = ManifestValidator.Validate([new ModuleManifest(Operation.Owner, new ModuleVersion(1, 0, 0), [], [new NeuronRoleDescriptor(Operation.EntryRole, NeuronScope.Workspace, Operation.Owner)], [Operation], [], [], [], [], [])]);
            var registry = new ModuleRegistry();
            registry.Resolve(modules.Modules);
            var store = new InMemoryActivityStore();
            Gateway = new OperationGateway(registry, new RefusedPolicy(), new EndpointResolver(modules), new CountingDispatcher(this), store, new ActivityProjectionService(store), new OperationTypeBindings([OperationTypeBinding.For<RefusedInput, RefusedResult>(Operation, new RefusedCanonicalizer())]));
        }
        public OperationDescriptor Operation { get; }
        public OperationGateway Gateway { get; private set; }
        public int Dispatches { get; private set; }
        private sealed class CountingDispatcher(RefusedGatewayFixture fixture) : IEntryOperationDispatcher
        {
            public Task DispatchAsync<T>(EndpointAddress endpoint, OperationInvocation<T> invocation, ActivityContext context, CancellationToken cancellationToken) where T : class
            {
                fixture.Dispatches++;
                return Task.CompletedTask;
            }
        }
        private sealed class RefusedPolicy : IWorkspacePolicyEvaluator
        {
            public PolicyDecision AuthorizeOperation(WorkspaceContext caller, OperationDescriptor operation) => PolicyDecision.Refused;
            public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request) => PolicyDecision.Refused;
            public PolicyDecision AuthorizeCapability(ActivityContext context, Brain.Abstractions.Capabilities.CapabilityDescriptor capability) => PolicyDecision.Refused;
        }
        private sealed class RefusedCanonicalizer : IIdempotencyInputCanonicalizer<RefusedInput> { public string Canonicalize(RefusedInput input) => input.Value; }
    }
    private sealed record RefusedInput(string Value);
    private sealed record RefusedResult;
}

#pragma warning restore IDE1006
