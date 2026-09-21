using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Map;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Map;

public sealed class MapFacts
{
    [Fact]
    public async Task SetWritesZoom()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IMap>("hq").Set(47.6, -122.3, 10, [new MapMarker("m", 47.6, -122.3, "SEA")]);
        Assert.Equal(10, (await brain.Get<IMap>("hq").Read()).Zoom);
    }
}
