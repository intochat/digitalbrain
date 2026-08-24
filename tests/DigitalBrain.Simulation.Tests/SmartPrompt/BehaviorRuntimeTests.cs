using DigitalBrain.SmartPrompt;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

[Collection(SimulationCollection.Name)]
public sealed class BehaviorRuntimeTests(SimulationFixture fixture)
{
    [Fact]
    public async Task Activated_X_behavior_routes_one_shared_event_to_one_linked_chart_point()
    {
        var brain = fixture.Sim.Brain;
        var definition = brain.GetEntity<IBehaviorDefinition>("bitcoin-tracker-test");
        var example = BehaviorExamples.Find("bitcoin-tracker")!;

        var compilation = await definition.Save(example.Source);
        Assert.True(compilation.Success);
        var report = await definition.Test();
        Assert.True(report.AllGreen, string.Join(Environment.NewLine, report.Failures));
        await definition.Activate();

        var ingress = fixture.Sim.Grains.GetGrain<IBehaviorIngress>(BehaviorIngressNames.Shared);
        var post = new BehaviorEvent(
            "post-42",
            "x.post",
            "elonmusk",
            "Bitcoin reaches 95000",
            95000,
            "https://x.com/elonmusk/status/42",
            DateTimeOffset.Parse("2026-08-25T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        await ingress.Publish(post);
        await ingress.Publish(post);

        var chart = brain.GetEntity<IChart>("bitcoin_tracker");
        var state = await WaitForChart(chart, static candidate => candidate.Points.Count == 1);
        var point = Assert.Single(state.Points);
        Assert.Equal(95000, point.Value);
        Assert.Equal(post.SourceUri, point.SourceUri);
        Assert.Equal(post.EventId, point.EventId);
        Assert.Contains("Test assistant reply", point.Description, StringComparison.Ordinal);
    }

    private static async Task<ChartState> WaitForChart(IChart chart, Func<ChartState, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            if (await chart.Read() is { } state && predicate(state))
            {
                return state;
            }
            await Task.Delay(50, timeout.Token);
        }
        throw new TimeoutException("The behavior chart was not updated.");
    }
}
