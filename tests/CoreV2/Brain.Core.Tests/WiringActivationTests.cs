using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Abstractions.Wiring;
using Brain.Core.Endpoints;
using Brain.Core.Graph;
using Brain.Core.Modules;
using Brain.Core.Policy;
using Brain.Core.Wiring;
using Xunit;

namespace Brain.Core.Tests;

public sealed class WiringActivationTests
{
    [Fact]
    public async Task IncompleteMultiShardActivationIsNotVisibleToGraphResolution()
    {
        var fixture = new ActivationFixture();
        var activation = await fixture.Activations.StartApplyAsync(fixture.Version, fixture.Context);

        await fixture.Activations.StageOneShardAsync(activation.Id);
        var resolution = await fixture.ResolveSourceOneAsync();

        Assert.Empty(resolution.Deliveries);
        Assert.Equal(WiringActivationStatus.Staging, await fixture.Activations.StatusAsync(activation.Id));
    }

    [Fact]
    public async Task CompletedActivationExposesAllStagedRoutesTogether()
    {
        var fixture = new ActivationFixture();

        var activation = await fixture.Activations.ApplyAsync(fixture.Version, fixture.Context);

        Assert.Equal(WiringActivationStatus.Active, await fixture.Activations.StatusAsync(activation.Id));
        Assert.Single((await fixture.ResolveSourceOneAsync()).Deliveries);
        Assert.Single((await fixture.ResolveSourceTwoAsync()).Deliveries);
    }

    [Fact]
    public async Task RetryStagesOnlyMissingShardAfterFailureAndNeverExposesPartialRoute()
    {
        var fixture = new ActivationFixture(failSecondShardOnce: true);

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Activations.ApplyAsync(fixture.Version, fixture.Context));
        Assert.Contains("staging", failed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await fixture.ResolveSourceOneAsync()).Deliveries);
        Assert.Equal(WiringActivationStatus.Failed, await fixture.Activations.StatusAsync(fixture.Activations.CurrentId));

        var active = await fixture.Activations.ApplyAsync(fixture.Version, fixture.Context);

        Assert.Equal(WiringActivationStatus.Active, active.Status);
        Assert.Equal(1, fixture.StageAttemptsForSourceOne);
        Assert.Equal(2, fixture.StageAttemptsForSourceTwo);
        Assert.Single((await fixture.ResolveSourceOneAsync()).Deliveries);
        Assert.Single((await fixture.ResolveSourceTwoAsync()).Deliveries);
    }

    private sealed class ActivationFixture
    {
        private readonly ModuleSet _modules;
        private readonly GraphShardDirectory _directory;
        private readonly EndpointResolver _resolver;
        private readonly WorkspacePolicyEvaluator _policy;
        private readonly Dictionary<NeuronRoleId, int> _attempts = [];
        private bool _failSecondShardOnce;

        public ActivationFixture(bool failSecondShardOnce = false)
        {
            _failSecondShardOnce = failSecondShardOnce;
            Context = new ActivityContext(new WorkspaceId("workspace/one"), new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("correlation/one"));
            Operation = new OperationDescriptor(new OperationId("proof/operate@1"), new ContractId("proof/requested@1"), new ContractId("proof/result@1"), new NeuronRoleId("proof.entry"), new ModuleId("proof"), new ContractVersion(1));
            SourceOne = new NeuronRoleId("proof.source-one");
            SourceTwo = new NeuronRoleId("proof.source-two");
            Target = new NeuronRoleId("proof.target");
            Produced = new ContractId("proof/produced@1");
            Version = new WiringVersion(WiringId.New(), 1, null, Context.Activity, Operation.Id, Operation.Version,
                [new WiringRoute(SourceOne, Target, Produced, new WiringSlotId("source-one"), null), new WiringRoute(SourceTwo, Target, Produced, new WiringSlotId("source-two"), null)], [], []);
            _modules = ManifestValidator.Validate(
            [
                new ModuleManifest(new ModuleId("proof"), new ModuleVersion(1, 0, 0), [],
                    [new NeuronRoleDescriptor(Operation.EntryRole, NeuronScope.Workspace, new ModuleId("proof")), new NeuronRoleDescriptor(SourceOne, NeuronScope.Workspace, new ModuleId("proof")), new NeuronRoleDescriptor(SourceTwo, NeuronScope.Workspace, new ModuleId("proof")), new NeuronRoleDescriptor(Target, NeuronScope.Workspace, new ModuleId("proof"))],
                    [Operation], [new EventDescriptor(Produced, new ModuleId("proof"), typeof(ProducedEvent), EventVisibility.Published)], [Produced], [], [], []),
            ]);
            _policy = new WorkspacePolicyEvaluator(_modules);
            _resolver = new EndpointResolver(_modules);
            _directory = new GraphShardDirectory(new GraphShardResolver());
            Activations = new WiringActivationGrain(_modules, _policy, _resolver, _directory, BeforeStage);
        }

        public ActivityContext Context { get; }
        public OperationDescriptor Operation { get; }
        public NeuronRoleId SourceOne { get; }
        public NeuronRoleId SourceTwo { get; }
        public NeuronRoleId Target { get; }
        public ContractId Produced { get; }
        public WiringVersion Version { get; }
        public WiringActivationGrain Activations { get; }
        public BrainActivityId CurrentId => Activations.CurrentId;
        public int StageAttemptsForSourceOne => _attempts.GetValueOrDefault(SourceOne);
        public int StageAttemptsForSourceTwo => _attempts.GetValueOrDefault(SourceTwo);

        public Task<GraphResolution> ResolveSourceOneAsync() => ResolveAsync(SourceOne);
        public Task<GraphResolution> ResolveSourceTwoAsync() => ResolveAsync(SourceTwo);

        private Task<GraphResolution> ResolveAsync(NeuronRoleId role)
        {
            var caller = new WorkspaceContext(Context.Workspace, Context.Principal, isServicePrincipal: false);
            var endpoint = _resolver.Resolve(new NeuronRoleDescriptor(role, NeuronScope.Workspace, new ModuleId("proof")), caller);
            return _directory.Open(endpoint, _modules, _policy).ResolveAsync(endpoint, Produced);
        }

        private void BeforeStage(NeuronRoleId sourceRole)
        {
            _attempts[sourceRole] = _attempts.GetValueOrDefault(sourceRole) + 1;
            if (_failSecondShardOnce && sourceRole == SourceTwo)
            {
                _failSecondShardOnce = false;
                throw new InvalidOperationException("staging failed");
            }
        }
    }

    private sealed class ProducedEvent : IDomainEvent;
}
