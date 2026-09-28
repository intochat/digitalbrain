using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chart;
using DigitalBrain.Flutter.Chart.Signals;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Chart;

public sealed class ChartFacts
{
    [Fact]
    public async Task RenderPublishesChangedAndReadMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var chart = brain.Get<IChart>("btc");
        await using var frames = await brain.Observe<ChartChanged>(chart, ct);
        await chart.Render("BTC", "line", [new ChartPoint("t0", "open", 64000)]);
        var changed = await frames.NextAsync(ct: ct);
        Assert.Equal("btc", changed.Name);
        Assert.Equal("BTC", changed.Title);
        Assert.Equal(1, changed.Version);
        var state = await chart.Read();
        Assert.Equal("line", state.Kind);
        Assert.Equal(64000, state.Points[0].Value);
    }

    [Fact]
    public async Task AppendIsIdempotentOnEventId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>()
            .StartAsync(ct);
        var chart = brain.Get<IChart>("series");
        await chart.Render("S", "bar", []);
        await chart.Append(new ChartPoint("e1", "a", 1));
        await chart.Append(new ChartPoint("e1", "a", 9));
        Assert.Single((await chart.Read()).Points);
        Assert.Equal(1, (await chart.Read()).Points[0].Value);
    }
}