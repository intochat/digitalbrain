using DigitalBrain.Abstractions.Identity;
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
    public async Task RendererWritesAPrincipalScopedChartWithoutAGrant()
    {
        var name = PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), "sales");
        var cancellationToken = TestContext.Current.CancellationToken;

        await fixture.Sim.Brain.FireAsync<IUIRenderer>(
            name,
            new ChartPoint("series-a", "jan", 1),
            cancellationToken);

        var state = await PollUntilPresentAsync(
            () => fixture.Sim.Brain.GetEntity<IChart>(name).Read(),
            state => state.Points.Count > 0,
            cancellationToken);

        Assert.Single(state.Points);
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
