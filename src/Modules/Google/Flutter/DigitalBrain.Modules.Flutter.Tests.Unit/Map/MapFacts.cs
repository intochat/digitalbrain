using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Map;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Map;

public sealed class MapFacts
{
    [Fact]
    public async Task SetWritesCentreZoomAndMarkers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        await brain.Get<IMap>("hq").Set(47.6, -122.3, 10, [new MapMarker("m", 47.6, -122.3, "SEA")]);
        var state = await brain.Get<IMap>("hq").Read();
        Assert.Equal(47.6, state.Lat);
        Assert.Equal(-122.3, state.Lng);
        Assert.Equal(10, state.Zoom);
        Assert.Equal(new MapMarker("m", 47.6, -122.3, "SEA"), Assert.Single(state.Markers));
    }
}