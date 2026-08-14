using System.Collections.Immutable;
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
using Xunit;

namespace Brain.Core.Tests;

public sealed class BrainGraphShardTests
{
    [Fact]
    public async Task ReplacePreservesKeySourceContractScopeAndSlot()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var installed = await fixture.Graph.InstallAsync(fixture.Request);

        var revised = await fixture.Graph.ReplaceAsync(
            installed.Key,
            fixture.Request with { Target = fixture.AssessmentTarget, Reshape = fixture.ToAssessment });

        Assert.Equal(installed.Key, revised.Key);
        Assert.Equal(installed.Source, revised.Source);
        Assert.Equal(installed.Contract, revised.Contract);
        Assert.Equal(installed.Scope, revised.Scope);
        Assert.Equal(installed.WiringSlot, revised.WiringSlot);
        Assert.NotEqual(installed.Target, revised.Target);
        Assert.Equal(2, revised.Revision);
    }

    [Fact]
    public async Task RetiredSynapseDoesNotResolveAndItsHistoryRemains()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var installed = await fixture.Graph.InstallAsync(fixture.Request);

        await fixture.Graph.RetireAsync(installed.Key, GraphReason.ManualRetire, fixture.Context);

        var resolution = await fixture.Graph.ResolveAsync(fixture.Source, fixture.Finished);
        var history = await fixture.Graph.HistoryAsync(installed.Key);
        Assert.Empty(resolution.Deliveries);
        Assert.Equal(2, history.Count);
        Assert.Equal(SynapseRevisionStatus.Retired, history[^1].Status);
    }

    [Fact]
    public async Task ReinstallAfterRetireReusesTheSameKeyWithANewLiveRevision()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var installed = await fixture.Graph.InstallAsync(fixture.Request);
        await fixture.Graph.RetireAsync(installed.Key, GraphReason.ManualRetire, fixture.Context);

        var reinstalled = await fixture.Graph.InstallAsync(fixture.Request);

        Assert.Equal(installed.Key, reinstalled.Key);
        Assert.Equal(3, reinstalled.Revision);
        Assert.Equal(SynapseRevisionStatus.Live, reinstalled.Status);
    }

    [Fact]
    public void DifferentSourceEndpointsMapToDistinctDeterministicShards()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var otherSource = fixture.Source with { Role = new NeuronRoleId("proof.other-source") };

        var first = fixture.Shards.Resolve(fixture.Source);
        var again = fixture.Shards.Resolve(fixture.Source);
        var other = fixture.Shards.Resolve(otherSource);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public async Task SameSourceHandlesShareOneAuthoritativeHistoryAndOtherSourceShardsAreIsolated()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var sameSourceHandle = fixture.Open(fixture.Source);
        var otherSource = fixture.Source with { Role = new NeuronRoleId("proof.other-source") };
        var otherSourceHandle = fixture.Open(otherSource);
        var installed = await fixture.Graph.InstallAsync(fixture.Request);
        await sameSourceHandle.RetireAsync(installed.Key, GraphReason.ManualRetire, fixture.Context);
        var reinstalled = await fixture.Graph.InstallAsync(fixture.Request);
        var otherInstalled = await otherSourceHandle.InstallAsync(fixture.Request with { Source = otherSource });

        Assert.Equal(installed.Key, reinstalled.Key);
        Assert.Equal(3, reinstalled.Revision);
        Assert.Single((await sameSourceHandle.ResolveAsync(fixture.Source, fixture.Finished)).Deliveries);
        Assert.Single((await otherSourceHandle.ResolveAsync(otherSource, fixture.Finished)).Deliveries);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => otherSourceHandle.HistoryAsync(reinstalled.Key));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with { Source = otherSource }));
        Assert.NotEqual(reinstalled.Key, otherInstalled.Key);
    }

    [Fact]
    public void DelimiterContainingEndpointFieldsProduceDistinctCanonicalShardKeys()
    {
        var resolver = new GraphShardResolver();
        var first = new EndpointAddress(new WorkspaceId("a|b"), new ModuleId("c"), new NeuronRoleId("d"), "e");
        var second = new EndpointAddress(new WorkspaceId("a"), new ModuleId("b"), new NeuronRoleId("c"), "d|e");

        Assert.NotEqual(resolver.Resolve(first), resolver.Resolve(second));
    }

    [Fact]
    public async Task ResolutionContainsOnlyTheLatestLiveRevisionSnapshot()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var installed = await fixture.Graph.InstallAsync(fixture.Request);
        var revised = await fixture.Graph.ReplaceAsync(installed.Key, fixture.Request with { Target = fixture.AssessmentTarget, Reshape = fixture.ToAssessment });

        var resolution = await fixture.Graph.ResolveAsync(fixture.Source, fixture.Finished);

        var delivery = Assert.Single(resolution.Deliveries);
        Assert.Equal(revised.Key, delivery.SynapseKey);
        Assert.Equal(revised.Revision, delivery.SynapseRevision);
        Assert.Equal(fixture.AssessmentTarget, delivery.Target);
        Assert.Equal(fixture.Assessed, delivery.OutputContract);
    }

    [Fact]
    public async Task SameModuleInternalEventIsAllowed()
    {
        var fixture = new GraphFixture(EventVisibility.Internal);

        var installed = await fixture.Graph.InstallAsync(fixture.Request);

        Assert.Equal(1, installed.Revision);
    }

    [Fact]
    public async Task CrossModuleInternalEventIsRejectedBeforeAnyRevisionIsWritten()
    {
        var fixture = new GraphFixture(EventVisibility.Internal, useWorkspacePolicy: true);

        await Assert.ThrowsAsync<GraphValidationException>(() =>
            fixture.Graph.InstallAsync(fixture.Request with { Target = fixture.AssessmentTarget, Reshape = fixture.ToAssessment }));

        Assert.Equal(0, await fixture.Graph.RevisionCountAsync());
    }

    [Fact]
    public async Task ProductionPolicyAllowsPublishedCrossModuleRouteToAnInstalledTargetRole()
    {
        var fixture = new GraphFixture(EventVisibility.Published, useWorkspacePolicy: true);

        var installed = await fixture.Graph.InstallAsync(fixture.Request with
        {
            Target = fixture.AssessmentTarget,
            Reshape = fixture.ToAssessment,
        });

        Assert.Equal(fixture.AssessmentTarget, installed.Target);
    }

    [Theory]
    [InlineData(PolicyDecision.Refused)]
    [InlineData(PolicyDecision.ConfirmationRequired)]
    public async Task NonAllowedPolicyDecisionWritesNoRevisions(PolicyDecision decision)
    {
        var fixture = new GraphFixture(EventVisibility.Published, decision);

        await Assert.ThrowsAsync<GraphPolicyException>(() => fixture.Graph.InstallAsync(fixture.Request));

        Assert.Equal(0, await fixture.Graph.RevisionCountAsync());
    }

    [Fact]
    public async Task InvalidSourceEventTargetAcceptanceAndReshapeDeclarationsWriteNoRevisions()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var undeclared = new ContractId("proof/missing@1");

        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with { Contract = undeclared }));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with { Target = fixture.AssessmentTarget }));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with
        {
            Target = fixture.AssessmentTarget,
            Reshape = fixture.ToAssessment with { InputEvent = fixture.Assessed },
        }));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with
        {
            Target = fixture.AssessmentTarget,
            Reshape = fixture.ToAssessment with { OutputEvent = new ContractId("proof/missing@1") },
        }));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with
        {
            Target = fixture.AssessmentTarget,
            Reshape = fixture.ToAssessment with { Owner = new ModuleId("assessment") },
        }));

        Assert.Equal(0, await fixture.Graph.RevisionCountAsync());
    }

    [Fact]
    public async Task DefaultStableDimensionsAreRejectedBeforeStateMutation()
    {
        var fixture = new GraphFixture(EventVisibility.Published);

        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with
        {
            Scope = string.Empty,
        }));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with
        {
            WiringSlot = default,
        }));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with
        {
            Source = fixture.Source with { ScopeToken = string.Empty },
        }));

        Assert.Equal(0, await fixture.Graph.RevisionCountAsync());
    }

    [Fact]
    public async Task WorkspaceScopedSourceAndTargetRejectNonWorkspaceTokensBeforeStateMutation()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var invalidSource = fixture.Source with { ScopeToken = "principal/alice" };

        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Open(invalidSource).InstallAsync(fixture.Request with
        {
            Source = invalidSource,
        }));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with
        {
            Target = fixture.ProofTarget with { ScopeToken = "principal/alice" },
        }));

        Assert.Equal(0, await fixture.Graph.RevisionCountAsync());
    }

    [Fact]
    public async Task PrincipalScopedSourceAndTargetRequireTheVerifiedPrincipalToken()
    {
        var fixture = new GraphFixture(EventVisibility.Published, roleScope: NeuronScope.Principal);
        var invalidSource = fixture.Source with { ScopeToken = "workspace" };

        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Open(invalidSource).InstallAsync(fixture.Request with
        {
            Source = invalidSource,
        }));
        await Assert.ThrowsAsync<GraphValidationException>(() => fixture.Graph.InstallAsync(fixture.Request with
        {
            Target = fixture.ProofTarget with { ScopeToken = "principal/bob" },
        }));

        Assert.Equal(0, await fixture.Graph.RevisionCountAsync());
    }

    [Fact]
    public async Task HistoryAndResolutionAreImmutableSnapshots()
    {
        var fixture = new GraphFixture(EventVisibility.Published);
        var installed = await fixture.Graph.InstallAsync(fixture.Request);
        var history = await fixture.Graph.HistoryAsync(installed.Key);
        var resolution = await fixture.Graph.ResolveAsync(fixture.Source, fixture.Finished);

        Assert.IsAssignableFrom<IImmutableList<SynapseRevision>>(history);
        Assert.IsAssignableFrom<IImmutableList<GraphDeliverySnapshot>>(resolution.Deliveries);
        Assert.Single(history);
        Assert.Single(resolution.Deliveries);
    }

    private sealed class GraphFixture
    {
        private readonly ModuleSet _modules;
        private readonly IWorkspacePolicyEvaluator _policy;

        public GraphFixture(
            EventVisibility visibility,
            PolicyDecision decision = PolicyDecision.Allowed,
            bool useWorkspacePolicy = false,
            NeuronScope roleScope = NeuronScope.Workspace)
        {
            Finished = new ContractId("proof/finished@1");
            Assessed = new ContractId("proof/assessed@1");
            var scopeToken = roleScope == NeuronScope.Workspace ? "workspace" : "principal/alice";
            Source = new EndpointAddress(new WorkspaceId("workspace/one"), new ModuleId("proof"), new NeuronRoleId("proof.source"), scopeToken);
            ProofTarget = new EndpointAddress(Source.Workspace, Source.Module, new NeuronRoleId("proof.target"), "workspace");
            AssessmentTarget = new EndpointAddress(Source.Workspace, new ModuleId("assessment"), new NeuronRoleId("assessment.target"), scopeToken);
            Context = new ActivityContext(Source.Workspace, new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("correlation/one"));
            ToAssessment = new ReshapeDescriptor(Finished, Assessed, Source.Module);
            _modules = ManifestValidator.Validate(
            [
                new ModuleManifest(
                    Source.Module,
                    new ModuleVersion(1, 0, 0),
                    [],
                    [
                        new NeuronRoleDescriptor(Source.Role, roleScope, Source.Module),
                        new NeuronRoleDescriptor(ProofTarget.Role, roleScope, Source.Module),
                        new NeuronRoleDescriptor(new NeuronRoleId("proof.other-source"), roleScope, Source.Module),
                    ],
                    [],
                    [
                        new EventDescriptor(Finished, Source.Module, typeof(ProofFinished), visibility),
                        new EventDescriptor(Assessed, Source.Module, typeof(ProofAssessed), EventVisibility.Published),
                    ],
                    [Finished],
                    [ToAssessment],
                    [],
                    []),
                new ModuleManifest(
                    AssessmentTarget.Module,
                    new ModuleVersion(1, 0, 0),
                    [],
                    [new NeuronRoleDescriptor(AssessmentTarget.Role, roleScope, AssessmentTarget.Module)],
                    [],
                    [],
                    [Assessed],
                    [],
                    [],
                    []),
            ]);
            Shards = new GraphShardResolver();
            Directory = new GraphShardDirectory(Shards);
            _policy = useWorkspacePolicy ? new WorkspacePolicyEvaluator(_modules) : new FixedPolicyEvaluator(decision);
            Graph = Open(Source);
            Request = new SynapseChangeRequest(Source, Finished, ProofTarget, roleScope == NeuronScope.Workspace ? "workspace" : "principal", new WiringSlotId("proof-finished"), null, Context);
        }

        public ContractId Finished { get; }

        public ContractId Assessed { get; }

        public EndpointAddress Source { get; }

        public EndpointAddress ProofTarget { get; }

        public EndpointAddress AssessmentTarget { get; }

        public ActivityContext Context { get; }

        public ReshapeDescriptor ToAssessment { get; }

        public GraphShardResolver Shards { get; }

        public GraphShardDirectory Directory { get; }

        public BrainGraphShardGrain Graph { get; }

        public SynapseChangeRequest Request { get; }

        public BrainGraphShardGrain Open(EndpointAddress source)
            => Directory.Open(source, _modules, _policy);
    }

    private sealed class FixedPolicyEvaluator(PolicyDecision decision) : IWorkspacePolicyEvaluator
    {
        public PolicyDecision AuthorizeOperation(WorkspaceContext caller, Brain.Abstractions.Operations.OperationDescriptor operation)
            => PolicyDecision.Refused;

        public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request) => decision;
    }

    private sealed class ProofFinished : IDomainEvent;

    private sealed class ProofAssessed : IDomainEvent;
}
