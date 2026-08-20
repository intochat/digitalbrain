using DigitalBrain.UI;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(E2ECollection.Name)]
public sealed class FabricSurfaceTests(AppHostFixture fixture)
{
    // C2 review gap 1: nothing pinned state recovery on the REAL Default blob provider —
    // EntityTests round-trip inside one activation in memory. Deactivate everything idle,
    // then prove the chart re-reads its points from grainstate blobs.
    [Fact]
    public async Task RendererWrittenChartStateSurvivesActivationCollection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var brain = fixture.BrainFor($"dur{Guid.NewGuid():N}"[..12]);
        var name = "chart-durability";

        await brain.FireAsync<IUIRenderer>(name, new ChartPoint("series", "before", 41), cancellationToken);
        var written = await brain.GetEntity<IChart>(name).Read();
        Assert.NotNull(written);
        Assert.Single(written!.Points);

        var management = (await fixture.GrainsAsync()).GetGrain<IManagementGrain>(0);
        await management.ForceActivationCollection(TimeSpan.Zero);

        var survived = await brain.GetEntity<IChart>(name).Read();
        Assert.NotNull(survived);
        var point = Assert.Single(survived!.Points);
        Assert.Equal(41, point.Value);
    }
}
