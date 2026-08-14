using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Core.Endpoints;
using Brain.Core.Neurons;
using Brain.Core.Outbox;
using Xunit;

namespace Brain.Core.Tests;

public sealed class NeuronTurnTests
{
    [Fact]
    public async Task EmitCommitsStateJournalAndRouteSnapshotBeforeDispatch()
    {
        var fixture = Fixture.WithRoutes();

        var outcome = await fixture.Neuron.EmitProducedAsync(fixture.Context, TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.DeliveryCount);
        Assert.Equal(1, fixture.Store.State);
        var entry = Assert.Single(fixture.Store.Emissions);
        Assert.Equal(new ContractId("proof/produced@1"), entry.EventContract);
        Assert.Single(entry.Deliveries);
        Assert.Contains(fixture.Store.Journal, journal => journal.EventId == entry.EventId);
    }

    [Fact]
    public async Task DirectSendDoesNotQueryTheBrainGraph()
    {
        var fixture = Fixture.WithRoutes();

        await fixture.Neuron.SendToEntryAsync(fixture.Context, TestContext.Current.CancellationToken);

        Assert.Equal(0, fixture.Routes.ResolutionCount);
        Assert.Single(fixture.Store.DirectedMessages);
    }

    [Fact]
    public async Task FailedRouteResolutionLeavesNoStateJournalOrOutboxChange()
    {
        var fixture = Fixture.FailingRoutes();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Neuron.EmitProducedAsync(fixture.Context, TestContext.Current.CancellationToken));

        Assert.Equal(0, fixture.Store.State);
        Assert.Empty(fixture.Store.Journal);
        Assert.Empty(fixture.Store.Emissions);
        Assert.Empty(fixture.Store.DirectedMessages);
    }

    [Fact]
    public async Task InvalidRouteMetadataLeavesNoStateJournalOrOutboxChange()
    {
        var fixture = Fixture.WithRoutes();
        fixture.Routes.ReturnedRoute!.Revision = 0;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Neuron.EmitProducedAsync(fixture.Context, TestContext.Current.CancellationToken));

        Assert.Equal(0, fixture.Store.State);
        Assert.Empty(fixture.Store.Journal);
        Assert.Empty(fixture.Store.Emissions);
        Assert.Empty(fixture.Store.DirectedMessages);
    }

    [Fact]
    public async Task InvalidDirectContractLeavesNoStateJournalOrOutboxChange()
    {
        var fixture = Fixture.WithRoutes();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Neuron.SendToEntryAsync(fixture.Context, default, TestContext.Current.CancellationToken));

        Assert.Equal(0, fixture.Store.State);
        Assert.Empty(fixture.Store.Journal);
        Assert.Empty(fixture.Store.Emissions);
        Assert.Empty(fixture.Store.DirectedMessages);
    }

    [Fact]
    public void RouteAndSnapshotConstructionRejectInvalidMetadata()
    {
        var fixture = Fixture.WithRoutes();
        var input = new ContractId("proof/produced@1");
        var output = new ContractId("proof/consumed@1");

        Assert.Throws<ArgumentNullException>(() => new GraphRoute(
            null!, SynapseKey.New(), 1, input, output, reshape: null));
        Assert.Throws<ArgumentException>(() => new GraphRoute(
            fixture.TargetEndpoint, default, 1, input, output, reshape: null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphRoute(
            fixture.TargetEndpoint, SynapseKey.New(), 0, input, output, reshape: null));
        Assert.Throws<ArgumentException>(() => new GraphRoute(
            fixture.TargetEndpoint, SynapseKey.New(), 1, default, output, reshape: null));
        Assert.Throws<ArgumentException>(() => new GraphRoute(
            fixture.TargetEndpoint, SynapseKey.New(), 1, input, output, new ReshapeId()));

        Assert.Throws<ArgumentNullException>(() => new DeliverySnapshot(
            DeliveryId.New(), null!, SynapseKey.New(), 1, input, output, reshape: null));
        Assert.Throws<ArgumentException>(() => new DeliverySnapshot(
            DeliveryId.New(), fixture.TargetEndpoint, default, 1, input, output, reshape: null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeliverySnapshot(
            DeliveryId.New(), fixture.TargetEndpoint, SynapseKey.New(), 0, input, output, reshape: null));
        Assert.Throws<ArgumentException>(() => new DeliverySnapshot(
            DeliveryId.New(), fixture.TargetEndpoint, SynapseKey.New(), 1, default, output, reshape: null));
        Assert.Throws<ArgumentException>(() => new DeliverySnapshot(
            DeliveryId.New(), fixture.TargetEndpoint, SynapseKey.New(), 1, input, output, new ReshapeId()));
    }

    [Fact]
    public async Task ZeroRouteEmissionIsJournaledWithoutFabricatingARefusal()
    {
        var fixture = Fixture.WithoutRoutes();

        var outcome = await fixture.Neuron.EmitProducedAsync(fixture.Context, TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.DeliveryCount);
        var entry = Assert.Single(fixture.Store.Emissions);
        Assert.Empty(entry.Deliveries);
        Assert.Single(fixture.Store.Journal);
    }

    [Fact]
    public async Task ResolverMutationAfterReturnCannotAlterPersistedDeliverySnapshot()
    {
        var fixture = Fixture.WithRoutes();

        await fixture.Neuron.EmitProducedAsync(fixture.Context, TestContext.Current.CancellationToken);
        fixture.Routes.ReturnedRoute!.Target = fixture.OtherEndpoint;

        var entry = Assert.Single(fixture.Store.Emissions);
        Assert.Equal(fixture.TargetEndpoint, Assert.Single(entry.Deliveries).Target);
    }

    [Fact]
    public void RuntimeRecordsHaveNoGenericPayloadEscapeHatches()
    {
        var runtime = Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../")),
            "src",
            "CoreV2",
            "Brain.Core");
        var files = new[]
        {
            Path.Combine(runtime, "Neurons", "NeuronTurn.cs"),
            Path.Combine(runtime, "Outbox", "OutboxEntry.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("JsonElement", source, StringComparison.Ordinal);
            Assert.DoesNotContain("object", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Entity", source, StringComparison.Ordinal);
        }
    }

    private sealed class Fixture
    {
        private Fixture(RecordingRoutes routes)
        {
            SourceEndpoint = new EndpointAddress(
                new WorkspaceId("workspace/proof"),
                new ModuleId("proof"),
                new NeuronRoleId("proof.source"),
                "workspace");
            TargetEndpoint = new EndpointAddress(
                new WorkspaceId("workspace/proof"),
                new ModuleId("proof"),
                new NeuronRoleId("proof.target"),
                "workspace");
            OtherEndpoint = new EndpointAddress(
                new WorkspaceId("workspace/proof"),
                new ModuleId("proof"),
                new NeuronRoleId("proof.other"),
                "workspace");
            Context = new ActivityContext(
                SourceEndpoint.Workspace,
                new PrincipalId("principal/alice"),
                BrainActivityId.New(),
                new CorrelationId("correlation/proof"));
            Store = new InMemoryOutboxStore<int>(0);
            Routes = routes;
            Neuron = new ProofNeuron(SourceEndpoint, Store, Routes);
        }

        public ActivityContext Context { get; }

        public EndpointAddress SourceEndpoint { get; }

        public EndpointAddress TargetEndpoint { get; }

        public EndpointAddress OtherEndpoint { get; }

        public InMemoryOutboxStore<int> Store { get; }

        public RecordingRoutes Routes { get; }

        public ProofNeuron Neuron { get; }

        public static Fixture WithRoutes()
        {
            var routes = new RecordingRoutes();
            var fixture = new Fixture(routes);
            routes.ReturnedRoute = new GraphRoute(
                fixture.TargetEndpoint,
                SynapseKey.New(),
                7,
                new ContractId("proof/produced@1"),
                new ContractId("proof/consumed@1"),
                reshape: null);
            return fixture;
        }

        public static Fixture WithoutRoutes() => new(new RecordingRoutes());

        public static Fixture FailingRoutes() => new(new RecordingRoutes { Failure = new InvalidOperationException("resolver failed") });
    }

    private sealed class ProofNeuron(
        EndpointAddress endpoint,
        InMemoryOutboxStore<int> store,
        IGraphRouteResolver routes)
        : BrainNeuron<int>(endpoint, store, routes)
    {
        public Task<EmissionOutcome> EmitProducedAsync(ActivityContext context, CancellationToken cancellationToken)
            => ExecuteTurnAsync(
                context,
                async turn =>
                {
                    turn.SetState(turn.State + 1);
                    return await EmitAsync(
                        turn,
                        new Produced(),
                        new ContractId("proof/produced@1"),
                        cancellationToken);
                },
                cancellationToken);

    public Task SendToEntryAsync(ActivityContext context, CancellationToken cancellationToken)
        => SendToEntryAsync(context, new ContractId("proof/entry@1"), cancellationToken);

        public Task SendToEntryAsync(
            ActivityContext context,
            ContractId contract,
            CancellationToken cancellationToken)
            => ExecuteTurnAsync(
                context,
                turn =>
                {
                    SendAsync(
                        turn,
                        new EndpointAddress(
                            context.Workspace,
                            new ModuleId("proof"),
                            new NeuronRoleId("proof.entry"),
                            "workspace"),
                        contract);
                    return Task.FromResult(0);
                },
                cancellationToken);
    }

    private sealed record Produced : IDomainEvent;

    private sealed class RecordingRoutes : IGraphRouteResolver
    {
        public int ResolutionCount { get; private set; }

        public Exception? Failure { get; init; }

        public GraphRoute? ReturnedRoute { get; set; }

        public Task<IReadOnlyList<GraphRoute>> ResolveAsync(
            EndpointAddress source,
            ContractId eventContract,
            ActivityContext context,
            CancellationToken cancellationToken)
        {
            ResolutionCount++;
            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyList<GraphRoute>>(Failure);
            }

            IReadOnlyList<GraphRoute> routes = ReturnedRoute is null ? [] : [ReturnedRoute];
            return Task.FromResult(routes);
        }
    }
}
