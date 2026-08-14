using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Wiring;
using Brain.Core.Endpoints;
using Brain.Core.Graph;
using Brain.Core.Modules;
using Brain.Core.Policy;
using Brain.Core.Wiring;
using Xunit;

namespace Brain.Proof.Tests;

#pragma warning disable IDE1006, IDE0300

public sealed class WiringAcceptanceTests
{
    [Fact]
    public void proposal_is_operation_major_scoped_and_contains_only_declarative_wiring_data()
    {
        var fixture = new WiringAcceptanceFixture();
        var proposal = new WiringProposal(fixture.Version);

        Assert.Equal(fixture.Operation.Id, proposal.Version.Operation);
        Assert.Equal(fixture.Operation.Version, proposal.Version.OperationMajor);
        var route = Assert.Single(proposal.Version.Routes);
        Assert.Equal(fixture.Source, route.SourceRole);
        Assert.Equal(fixture.Target, route.TargetRole);
        Assert.Equal(fixture.Produced, route.EventContract);
        Assert.Null(route.Reshape);
        Assert.Empty(proposal.Version.RequiredCapabilities);
        Assert.Empty(proposal.Version.PolicyPrerequisites);
    }

    [Fact]
    public async Task applying_principal_scoped_wiring_resolves_bob_only_without_copying_alice_activity_payload_result_or_capability_state()
    {
        var fixture = new WiringAcceptanceFixture();
        var bob = fixture.For("principal/bob");

        await fixture.Activations.ApplyAsync(fixture.Version, bob);

        Assert.Empty((await fixture.ResolveAsync("principal/alice")).Deliveries);
        var delivery = Assert.Single((await fixture.ResolveAsync("principal/bob")).Deliveries);
        Assert.Equal("principal/bob", delivery.Target.ScopeToken);
        Assert.DoesNotContain("alice", delivery.Target.ScopeToken, StringComparison.OrdinalIgnoreCase);

        await using var host = await Brain.Testing.BrainTestHost.StartAsync();
        var alice = host.Caller("workspace/wiring-runtime", "principal/alice");
        var aliceActivity = await host.Operations.InvokeAsync<Brain.Modules.Proof.Contracts.ProofInput, Brain.Modules.Proof.Contracts.ProofResult>(Brain.Modules.Proof.Contracts.ProofContracts.Run, new Brain.Modules.Proof.Contracts.ProofInput("alice-private-input"), alice, new IdempotencyKey("wiring/alice"), TestContext.Current.CancellationToken);
        var capabilityCalls = await host.CapabilityCallCountAsync();

        await host.ApplyPrincipalWiringAsync("workspace/wiring-runtime", "principal/bob");
        var bobEvidence = await host.PrincipalRuntimeEvidenceAsync("workspace/wiring-runtime", "principal/bob");

        Assert.Equal(capabilityCalls, await host.CapabilityCallCountAsync());
        Assert.Contains("scope/principal/bob", bobEvidence);
        Assert.Contains("route/workspace", bobEvidence);
        Assert.DoesNotContain(bobEvidence, item => item.Contains(aliceActivity.Activity.Value.ToString("N"), StringComparison.Ordinal));
        Assert.DoesNotContain(bobEvidence, item => item.Contains("alice-private-input", StringComparison.Ordinal));
        Assert.DoesNotContain(bobEvidence, item => item.Contains("classified/alice-private-input", StringComparison.Ordinal));
    }

    [Fact]
    public async Task multi_shard_wiring_is_not_visible_until_its_activation_is_active()
    {
        var fixture = new WiringAcceptanceFixture(multipleShards: true);
        var activation = await fixture.Activations.StartApplyAsync(fixture.Version, fixture.For("principal/bob"));

        await fixture.Activations.StageOneShardAsync(activation.Id);

        Assert.Empty((await fixture.ResolveAsync("principal/bob")).Deliveries);
        Assert.Equal(WiringActivationStatus.Staging, await fixture.Activations.StatusAsync(activation.Id));
    }

    private sealed class WiringAcceptanceFixture
    {
        private readonly ModuleSet _modules;
        private readonly EndpointResolver _resolver;
        private readonly GraphShardDirectory _directory;
        public WiringAcceptanceFixture(bool multipleShards = false)
        {
            var module = new ModuleId("proof");
            Source = new NeuronRoleId("proof.source");
            Target = new NeuronRoleId("proof.target");
            Produced = new ContractId("proof/produced@1");
            Operation = new OperationDescriptor(new OperationId("proof/run@1"), new ContractId("proof/input@1"), new ContractId("proof/result@1"), new NeuronRoleId("proof.entry"), module, new ContractVersion(1));
            var otherSource = new NeuronRoleId("proof.other-source");
            _modules = ManifestValidator.Validate([new ModuleManifest(module, new ModuleVersion(1, 0, 0), [],
                [new NeuronRoleDescriptor(Operation.EntryRole, NeuronScope.Principal, module), new NeuronRoleDescriptor(Source, NeuronScope.Principal, module), new NeuronRoleDescriptor(otherSource, NeuronScope.Principal, module), new NeuronRoleDescriptor(Target, NeuronScope.Principal, module)], [Operation],
                [new EventDescriptor(Produced, module, typeof(ProducedEvent), EventVisibility.Published)], [Produced], [], [], [])]);
            _resolver = new EndpointResolver(_modules);
            _directory = new GraphShardDirectory(new GraphShardResolver());
            Activations = new WiringActivationGrain(_modules, new WorkspacePolicyEvaluator(_modules), _resolver, _directory);
            var routes = multipleShards
                ? new[] { new WiringRoute(Source, Target, Produced, new WiringSlotId("one"), null), new WiringRoute(otherSource, Target, Produced, new WiringSlotId("two"), null) }
                : new[] { new WiringRoute(Source, Target, Produced, new WiringSlotId("one"), null) };
            Version = new WiringVersion(WiringId.New(), 1, null, BrainActivityId.New(), Operation.Id, Operation.Version, routes, [], []);
        }
        public NeuronRoleId Source { get; }
        public NeuronRoleId Target { get; }
        public ContractId Produced { get; }
        public OperationDescriptor Operation { get; }
        public WiringVersion Version { get; }
        public WiringActivationGrain Activations { get; }
        public ActivityContext For(string principal) => new(new WorkspaceId("workspace/wiring"), new PrincipalId(principal), Version.CauseActivity, new CorrelationId("wiring/one"));
        public Task<GraphResolution> ResolveAsync(string principal)
        {
            var caller = new WorkspaceContext(new WorkspaceId("workspace/wiring"), new PrincipalId(principal), false);
            var source = _resolver.Resolve(new NeuronRoleDescriptor(Source, NeuronScope.Principal, new ModuleId("proof")), caller);
            return _directory.Open(source, _modules, new WorkspacePolicyEvaluator(_modules)).ResolveAsync(source, Produced);
        }
        private sealed class ProducedEvent : IDomainEvent;
    }
}

#pragma warning restore IDE1006, IDE0300
