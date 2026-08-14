using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Policy;
using Brain.Core.Endpoints;
using Brain.Core.Graph;
using Brain.Core.Modules;
using Brain.Core.Policy;
using Brain.Modules.Proof.Contracts;
using Brain.Testing;
using Xunit;

namespace Brain.Proof.Tests;

#pragma warning disable IDE1006

public sealed class RewireAndRetireAcceptanceTests
{
    [Fact]
    public async Task proof_route_replacement_keeps_the_opaque_key_and_preserves_the_captured_old_delivery()
    {
        var fixture = new GraphAcceptanceFixture();
        var initial = await fixture.Graph.InstallAsync(fixture.Initial);
        var captured = Assert.Single((await fixture.Graph.ResolveAsync(fixture.Source, fixture.Produced)).Deliveries);

        var replacement = await fixture.Graph.ReplaceAsync(initial.Key, fixture.Initial with { Target = fixture.Assessment });

        var later = Assert.Single((await fixture.Graph.ResolveAsync(fixture.Source, fixture.Produced)).Deliveries);
        Assert.Equal(initial.Key, replacement.Key);
        Assert.NotEqual(captured.Target, later.Target);
        Assert.Equal(fixture.Summary, captured.Target);
        Assert.Equal(fixture.Assessment, later.Target);
        Assert.Equal(2, (await fixture.Graph.HistoryAsync(initial.Key)).Count);
    }

    [Fact]
    public async Task public_proof_flow_completes_initial_work_through_summary_and_later_work_through_assessment()
    {
        await using var host = await BrainTestHost.StartAsync();
        var caller = host.Caller("workspace/rewire", "principal/alice");
        var first = await host.Operations.InvokeAsync<ProofInput, ProofResult>(ProofContracts.Run, new ProofInput("first"), caller, new IdempotencyKey("rewire/first"), TestContext.Current.CancellationToken);
        var firstResult = await host.ReadResultAsync<ProofResult>(await host.Operations.ObserveAsync(first.Activity, caller, TestContext.Current.CancellationToken), caller);

        await host.Operations.InvokeAsync<CorrectionInput, CorrectionResult>(ProofContracts.Correct, new CorrectionInput("assessment"), caller, new IdempotencyKey("rewire/correct"), TestContext.Current.CancellationToken);
        var later = await host.Operations.InvokeAsync<ProofInput, ProofResult>(ProofContracts.Run, new ProofInput("later"), caller, new IdempotencyKey("rewire/later"), TestContext.Current.CancellationToken);
        var laterResult = await host.ReadResultAsync<ProofResult>(await host.Operations.ObserveAsync(later.Activity, caller, TestContext.Current.CancellationToken), caller);

        Assert.Equal("summary", firstResult.Route);
        Assert.Equal("assessment", laterResult.Route);
    }

    [Fact]
    public async Task retired_route_stops_later_resolution_while_test_only_history_remains_available()
    {
        var fixture = new GraphAcceptanceFixture();
        var installed = await fixture.Graph.InstallAsync(fixture.Initial);

        await fixture.Graph.RetireAsync(installed.Key, GraphReason.ManualRetire, fixture.Context);

        Assert.Empty((await fixture.Graph.ResolveAsync(fixture.Source, fixture.Produced)).Deliveries);
        Assert.Equal(SynapseRevisionStatus.Retired, (await fixture.Graph.HistoryAsync(installed.Key))[^1].Status);
    }

    private sealed class GraphAcceptanceFixture
    {
        private readonly ModuleSet _modules;
        public GraphAcceptanceFixture()
        {
            Source = new EndpointAddress(new WorkspaceId("workspace/graph"), new ModuleId("proof"), new NeuronRoleId("proof.source"), "workspace");
            Summary = new EndpointAddress(Source.Workspace, Source.Module, new NeuronRoleId("proof.summary"), "workspace");
            Assessment = new EndpointAddress(Source.Workspace, Source.Module, new NeuronRoleId("proof.assessment"), "workspace");
            Produced = new ContractId("proof/produced@1");
            Context = new ActivityContext(Source.Workspace, new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("graph/one"));
            _modules = ManifestValidator.Validate([new ModuleManifest(Source.Module, new ModuleVersion(1, 0, 0), [],
                [new NeuronRoleDescriptor(Source.Role, NeuronScope.Workspace, Source.Module), new NeuronRoleDescriptor(Summary.Role, NeuronScope.Workspace, Source.Module), new NeuronRoleDescriptor(Assessment.Role, NeuronScope.Workspace, Source.Module)], [],
                [new EventDescriptor(Produced, Source.Module, typeof(ProducedEvent), EventVisibility.Published)], [Produced], [], [], [])]);
            Graph = new GraphShardDirectory(new GraphShardResolver()).Open(Source, _modules, new WorkspacePolicyEvaluator(_modules));
            Initial = new SynapseChangeRequest(Source, Produced, Summary, "proof", new WiringSlotId("result"), null, Context);
        }
        public EndpointAddress Source { get; }
        public EndpointAddress Summary { get; }
        public EndpointAddress Assessment { get; }
        public ContractId Produced { get; }
        public ActivityContext Context { get; }
        public BrainGraphShardGrain Graph { get; }
        public SynapseChangeRequest Initial { get; }
        private sealed class ProducedEvent : IDomainEvent;
    }
}

#pragma warning restore IDE1006
