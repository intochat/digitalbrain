using DigitalBrain.Abstractions.Brain;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// The UI target shape: the renderer neuron is the one write path into the UI entities, and a
// renderer instance shares its name with the entity it fills (uirenderer:{name} → chart:{name}
// and surface:{name}).
[Collection(SimulationCollection.Name)]
public sealed class UIRendererTests(SimulationFixture fixture)
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task ChartPointFiredAtTheRendererLandsInTheChartEntity()
    {
        var name = fixture.Sim.UniqueId("chart");
        var cancellationToken = TestContext.Current.CancellationToken;

        // "chart-<8 hex>" never parses as a "{principal:N}.{local}" partitioned name and the
        // test client carries no verified principal, so the renderer's migrated write-path
        // grant check passes through its unattributed non-partitioned branch.
        await fixture.Sim.Brain.FireAsync<IUIRenderer>(
            name,
            new ChartPoint("series-a", "jan", 42),
            cancellationToken);

        var state = await PollUntilPresentAsync(
            () => fixture.Sim.Brain.GetEntity<IChart>(name).Read(),
            state => state.Points.Count > 0,
            cancellationToken);

        var point = Assert.Single(state.Points);
        Assert.Equal("series-a", point.Series);
        Assert.Equal("jan", point.Label);
        Assert.Equal(42, point.Value);
    }

    [Fact]
    public async Task OpenSurfaceFiredAtTheRendererLandsInTheSurfaceEntity()
    {
        var name = fixture.Sim.UniqueId("desk");
        var cancellationToken = TestContext.Current.CancellationToken;

        await fixture.Sim.Brain.FireAsync<IUIRenderer>(
            name,
            new OpenSurface(CommandId.New(), "home", "Home"),
            cancellationToken);

        var state = await PollUntilPresentAsync(
            () => fixture.Sim.Brain.GetEntity<ISurface>(name).Read(),
            state => state.Scenes.Count > 0,
            cancellationToken);

        var scene = Assert.Single(state.Scenes);
        Assert.Equal("home", scene.SurfaceKey);
        Assert.Equal("Home", scene.Title);
    }

    [Fact]
    public async Task ChartPointFiredAtTheRendererRegistersTheChartInTheOwnersBrain()
    {
        // A fresh owner keeps ResolveAsync("chart") unambiguous: the renderer's write is
        // silo-side (GrainFactory, not the client facade's GetEntity), so this pins that the
        // brain still learns about it and chart:{owner}/{name} becomes resolvable.
        var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("owner"));
        var name = fixture.Sim.UniqueId("chart");
        var cancellationToken = TestContext.Current.CancellationToken;

        await brain.FireAsync<IUIRenderer>(name, new ChartPoint("series-a", "jan", 1), cancellationToken);

        var resolved = await PollUntilPresentAsync(
            () => brain.ResolveAsync("chart", cancellationToken),
            reference => reference.Name == name,
            cancellationToken);

        Assert.Equal("chart", resolved.Type);
        Assert.Equal(BrainReferenceKind.Entity, resolved.Kind);
    }

    [Fact]
    public async Task RendererRefusesAPrincipalScopedChartWriteWithoutAVerifiedPrincipal()
    {
        // "{principal:N}.sales" IS a partitioned name, and this client call rides no verified
        // principal, so the write-path grant check (migrated from ChartNeuron into the
        // renderer) must refuse the write before it reaches the chart entity.
        var name = PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), "sales");
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => fixture.Sim.Brain.FireAsync<IUIRenderer>(
                name,
                new ChartPoint("series-a", "jan", 1),
                cancellationToken));

        Assert.Null(await fixture.Sim.Brain.GetEntity<IChart>(name).Read());
    }

    private static async Task<TState> PollUntilPresentAsync<TState>(
        Func<Task<TState?>> read,
        Func<TState, bool> settled,
        CancellationToken cancellationToken)
        where TState : class
    {
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await read() is { } state && settled(state))
            {
                return state;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"The entity did not hold the rendered state within {PollTimeout}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
