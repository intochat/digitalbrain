using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Brain;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Client;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// Every test uses a fresh owner: the brain is one grain per owner, and the shared "dev"
// owner accumulates registrations from every other suite in this collection.
[Collection(SimulationCollection.Name)]
public sealed class BrainTests(SimulationFixture fixture)
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task ActivatedNeuronRegistersItselfInTheBrain()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var name = fixture.Sim.UniqueId("chart");
        var cancellationToken = TestContext.Current.CancellationToken;

        await brain.FireAsync<IChart>(name, new ChartPoint("series-a", "jan", 1), cancellationToken);

        await PollForNodeAsync(brain, BrainReferenceKind.Neuron, "chart", name, cancellationToken);

        var contexts = await brain.ContextsAsync(cancellationToken);
        var defaultContext = Assert.Single(contexts, c => c.Name == BrainState.DefaultContext);
        Assert.Contains(defaultContext.Members, member => member.Name == name && member.Type == "chart");
    }

    [Fact]
    public async Task EntityUseThroughTheFacadeRegistersTheEntity()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var name = fixture.Sim.UniqueId("counter");
        var cancellationToken = TestContext.Current.CancellationToken;

        await brain.GetEntity<ICounterEntity>(name).Add(1);

        var node = await PollForNodeAsync(
            brain, BrainReferenceKind.Entity, "counterentity", name, cancellationToken);
        Assert.Equal(BrainReferenceKind.Entity, node.Kind);
    }

    [Fact]
    public async Task ResolveFindsTheCounterTouchedInTheDefaultContext()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var name = fixture.Sim.UniqueId("counter");
        var cancellationToken = TestContext.Current.CancellationToken;

        await brain.GetEntity<ICounterEntity>(name).Add(1);
        await PollForNodeAsync(brain, BrainReferenceKind.Entity, "counterentity", name, cancellationToken);

        var resolved = await brain.ResolveAsync("counter", cancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(name, resolved.Name);
        Assert.Equal("counterentity", resolved.Type);
    }

    [Fact]
    public async Task ChartTouchedInTheActiveContextWinsResolution()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var defaultChart = fixture.Sim.UniqueId("chart");
        var workChart = fixture.Sim.UniqueId("chart");
        var cancellationToken = TestContext.Current.CancellationToken;

        // The registrations are fire-and-forget, so each touch is confirmed landed before the
        // context switches — otherwise the first chart could race into the "work" context.
        await brain.GetEntity<IChartEntity>(defaultChart).Read();
        await PollForNodeAsync(
            brain, BrainReferenceKind.Entity, "chartentity", defaultChart, cancellationToken);

        await brain.UseContextAsync("work", cancellationToken);
        Assert.Equal("work", await brain.ActiveContextAsync(cancellationToken));

        await brain.GetEntity<IChartEntity>(workChart).Read();
        await PollForNodeAsync(
            brain, BrainReferenceKind.Entity, "chartentity", workChart, cancellationToken);

        // Re-touch the first chart in ITS context via the per-call override: it becomes the
        // most recently used node overall, so a contextless recency-only resolution would
        // return it and fail the assertion below.
        var reTouched = await BrainGrainOf(brain).Resolve(defaultChart, BrainState.DefaultContext);
        Assert.Equal(defaultChart, reTouched!.Name);

        var resolved = await brain.ResolveAsync("chart", cancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(workChart, resolved.Name);
    }

    [Fact]
    public async Task RepeatedUseInTheContextBiasesResolutionOverRecency()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var occasional = fixture.Sim.UniqueId("counter");
        var habitual = fixture.Sim.UniqueId("counter");
        var cancellationToken = TestContext.Current.CancellationToken;

        // The habitual registrations are poll-confirmed landed BEFORE the occasional counter
        // is touched, so the occasional one is strictly the most recent and only the tally
        // can make the habitual one win.
        await brain.GetEntity<ICounterEntity>(habitual).Add(1);
        await brain.GetEntity<ICounterEntity>(habitual).Add(1);
        await PollForTalliesAsync(
            brain,
            tallies => tallies.GetValueOrDefault($"counterentity/{habitual}") == 2,
            cancellationToken);

        await brain.GetEntity<ICounterEntity>(occasional).Add(1);
        await PollForTalliesAsync(
            brain,
            tallies => tallies.GetValueOrDefault($"counterentity/{occasional}") == 1,
            cancellationToken);

        var resolved = await brain.ResolveAsync("counter", cancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(habitual, resolved.Name);
    }

    [Fact]
    public async Task NodesCapEvictsTheLeastRecentlyUsedReference()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var grain = BrainGrainOf(brain);

        for (var i = 0; i <= BrainState.MaximumNodes; i++)
        {
            await grain.Register(new BrainReference(
                BrainReferenceKind.Entity,
                "counterentity",
                $"counter-{i:D3}",
                default));
        }

        var state = await grain.Read();
        Assert.NotNull(state);
        Assert.Equal(BrainState.MaximumNodes, state.Nodes.Count);
        Assert.DoesNotContain(state.Nodes, node => node.Name == "counter-000");

        var defaultContext = Assert.Single(state.Contexts);
        Assert.Equal(BrainState.MaximumNodes, defaultContext.Members.Count);
        Assert.DoesNotContain(
            defaultContext.Tallies.Keys,
            key => key.EndsWith("/counter-000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RouteMatchesTheSourceAndRoleAndReturnsNullOtherwise()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var grain = BrainGrainOf(brain);
        var connection = new Connection(
            NeuronId.For<IChart>(brain.Owner, fixture.Sim.UniqueId("chart")),
            "render",
            NeuronId.For<IChart>(brain.Owner, fixture.Sim.UniqueId("chart")));

        await grain.Connect(connection);

        Assert.Equal(connection, await grain.Route(connection.From, "render"));
        Assert.Null(await grain.Route(connection.From, "unknown"));

        // Source-aware: another neuron emitting the same alias is not captured by this wire.
        var stranger = NeuronId.For<IChart>(brain.Owner, fixture.Sim.UniqueId("chart"));
        Assert.Null(await grain.Route(stranger, "render"));

        var routed = Assert.Single(await grain.Connections(connection.From, "render"));
        Assert.Equal(connection, routed);

        await grain.Disconnect(connection);
        Assert.Null(await grain.Route(connection.From, "render"));
    }

    [Fact]
    public async Task ConnectRefusesSelfWiresCyclesAndDuplicateRoutes()
    {
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var grain = BrainGrainOf(brain);
        var a = NeuronId.For<IChart>(brain.Owner, fixture.Sim.UniqueId("chart"));
        var b = NeuronId.For<IChart>(brain.Owner, fixture.Sim.UniqueId("chart"));
        var c = NeuronId.For<IChart>(brain.Owner, fixture.Sim.UniqueId("chart"));

        // A self-route dispatches in place and recurses with no timeout.
        var selfWire = await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => grain.Connect(new Connection(a, "loop", a)));
        Assert.Contains("self-wire", selfWire.Message, StringComparison.Ordinal);

        await grain.Connect(new Connection(a, "first", b));
        await grain.Connect(new Connection(b, "second", c));

        // Routing is single-target: a second wire on the same (source, alias) is refused.
        var duplicate = await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => grain.Connect(new Connection(a, "first", c)));
        Assert.Contains("single-target", duplicate.Message, StringComparison.Ordinal);

        // Closing c -> a would cycle a -> b -> c -> a and deadlock the non-reentrant grains.
        var cycle = await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => grain.Connect(new Connection(c, "third", a)));
        Assert.Contains("cycle", cycle.Message, StringComparison.Ordinal);
        Assert.Contains($"{c} --third--> {a} --first--> {b} --second--> {c}", cycle.Message, StringComparison.Ordinal);

        // The refused wires left no trace: the acyclic pair still routes.
        Assert.NotNull(await grain.Route(a, "first"));
        Assert.Null(await grain.Route(c, "third"));
    }

    private IBrain BrainGrainOf(IDigitalBrain brain)
        => fixture.Sim.Grains.GetGrain<IBrain>(
            EntityId.For<IBrain>(brain.Owner, DigitalBrainNames.DefaultBrain).ToGrainId());

    private async Task<BrainReference> PollForNodeAsync(
        IDigitalBrain brain,
        BrainReferenceKind kind,
        string type,
        string name,
        CancellationToken cancellationToken)
    {
        var grain = BrainGrainOf(brain);
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await grain.Read();
            var node = state?.Nodes.FirstOrDefault(
                n => n.Kind == kind && n.Type == type && n.Name == name);
            if (node is not null)
            {
                return node;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                var known = string.Join(
                    ", ",
                    state?.Nodes.Select(n => $"{n.Kind}:{n.Type}/{n.Name}") ?? []);
                throw new TimeoutException(
                    $"{kind} node {type}/{name} was not registered within {PollTimeout}. Known: [{known}]");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task PollForTalliesAsync(
        IDigitalBrain brain,
        Func<IReadOnlyDictionary<string, int>, bool> settled,
        CancellationToken cancellationToken)
    {
        var grain = BrainGrainOf(brain);
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await grain.Read();
            var defaultContext = state?.Contexts.FirstOrDefault(
                c => c.Name == BrainState.DefaultContext);
            if (defaultContext is not null && settled(defaultContext.Tallies))
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                var seen = defaultContext is null
                    ? "(no default context)"
                    : string.Join(", ", defaultContext.Tallies.Select(t => $"{t.Key}={t.Value}"));
                throw new TimeoutException(
                    $"The default context tallies did not settle within {PollTimeout}. Saw: [{seen}]");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
