using DigitalBrain.SmartPrompt;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using DigitalBrain.Chat;
using Xunit;

namespace DigitalBrain.Simulation.Tests.SmartPrompt;

[Collection(SimulationCollection.Name)]
public sealed class BehaviorRuntimeTests(SimulationFixture fixture)
{
    [Fact]
    public async Task Behavior_catalog_persists_generated_behavior_names_without_duplicates()
    {
        var catalog = fixture.Sim.Brain.GetEntity<IBehaviorCatalog>("catalog");
        var name = $"generated-{Guid.NewGuid():N}";

        await catalog.Add(name);
        await catalog.Add(name);

        var state = await catalog.Read();
        Assert.NotNull(state);
        Assert.Equal(1, state!.Names.Count(candidate => candidate == name));
    }

    [Fact]
    public async Task All_eight_seeded_examples_execute_their_paired_fake_scenarios()
    {
        var brain = fixture.Sim.Brain;
        var ingress = fixture.Sim.Grains.GetGrain<IBehaviorIngress>(BehaviorIngressNames.Shared);
        var chat = brain.GetGrainProxy<IChat>("main");
        var beforeChat = (await chat.Read()).Turns.Count;

        foreach (var example in BehaviorExamples.All)
        {
            var definition = await brain.GetEntity<IBehaviorDefinition>(example.Name).Read();
            Assert.NotNull(definition);
            Assert.True(definition!.Active, example.Name);
            Assert.True(definition.LastTest?.AllGreen, example.Name);
            await ingress.Publish(FakeBehaviorEvents.Create(
                example.Name,
                $"paired-{example.Name}-{Guid.NewGuid():N}"));
        }

        var bitcoin = await brain.GetEntity<IChart>("bitcoin_tracker").Read();
        var portfolio = await brain.GetEntity<IChart>("portfolio").Read();
        var health = await brain.GetEntity<IChart>("health").Read();
        Assert.Contains(bitcoin!.Points, static point => point.Value == 95000 && point.SourceUri is not null);
        Assert.Contains(portfolio!.Points, static point => point.Value == 95000 && point.SourceUri is not null);
        Assert.Contains(health!.Points, static point => point.Value == 135 && point.SourceUri is not null);

        var addedTurns = (await chat.Read()).Turns.Skip(beforeChat).Select(static turn => turn.Text).ToArray();
        Assert.Contains(addedTurns, static text => text.StartsWith("Explain urgent work email:", StringComparison.Ordinal));
        Assert.Contains(addedTurns, static text => text.StartsWith("Prepare for a travel event:", StringComparison.Ordinal));
        Assert.Contains(addedTurns, static text => text.StartsWith("Summarize an incoming document:", StringComparison.Ordinal));
        Assert.Contains(addedTurns, static text => text.StartsWith("Triage a new issue:", StringComparison.Ordinal));
        Assert.Contains(addedTurns, static text => text.StartsWith("Remind me when I arrive home:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Activated_X_behavior_routes_one_shared_event_to_one_linked_chart_point()
    {
        var brain = fixture.Sim.Brain;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var chartName = $"bitcoin_tracker_{suffix}";
        var definition = brain.GetEntity<IBehaviorDefinition>($"bitcoin-tracker-{suffix}");
        var example = BehaviorExamples.Find("bitcoin-tracker")!;

        var compilation = await definition.Save(example.Source.Replace(
            "bitcoin_tracker",
            chartName,
            StringComparison.Ordinal));
        Assert.True(compilation.Success);
        var report = await definition.Test();
        Assert.True(report.AllGreen, string.Join(Environment.NewLine, report.Failures));
        await definition.Activate();

        var ingress = fixture.Sim.Grains.GetGrain<IBehaviorIngress>(BehaviorIngressNames.Shared);
        var post = new BehaviorEvent(
            $"post-{suffix}",
            "x.post",
            "elonmusk",
            "Bitcoin reaches 95000",
            95000,
            "https://x.com/elonmusk/status/42",
            DateTimeOffset.Parse("2026-08-25T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        await ingress.Publish(post);
        await ingress.Publish(post);

        var chart = brain.GetEntity<IChart>(chartName);
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
