using Ino.Core;
using Ino.Core.Hosting.Llm;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public class BddScenarioPromptCorpusTests
{
    [Fact]
    public void Groups_scenarios_by_neuron_tag()
    {
        var scenarios = new[]
        {
            Scenario("Plan a trip", "plan.*trip", new[] { "@neuron:travel.plan-trip" }),
            Scenario("Plan trip with dates", "plan.*trip.*tokyo", new[] { "@neuron:travel.plan-trip" }),
            Scenario("Find flights", "find.*flight", new[] { "@neuron:travel.find-flights" }),
        };

        var corpus = new BddScenarioPromptCorpus(scenarios);

        Assert.Equal(3, corpus.Count);
        Assert.Equal(2, corpus.ByNeuron[NeuronId.From("travel.plan-trip")].Count);
        Assert.Single(corpus.ByNeuron[NeuronId.From("travel.find-flights")]);
    }

    [Fact]
    public void Skips_untagged_scenarios()
    {
        var scenarios = new[]
        {
            Scenario("Plan a trip", "plan.*trip", new[] { "@neuron:travel.plan-trip" }),
            // Reactive narration mock — no @neuron: tag → not routable.
            Scenario("Price dropped", "price.*drop", new[] { "@reactive" }),
            Scenario("Ambient noise", "noise", Array.Empty<string>()),
        };

        var corpus = new BddScenarioPromptCorpus(scenarios);

        Assert.Equal(1, corpus.Count);
        Assert.Single(corpus.ByNeuron);
        Assert.Contains(NeuronId.From("travel.plan-trip"), corpus.ByNeuron.Keys);
    }

    [Fact]
    public void Pattern_carries_source_metadata_for_telemetry()
    {
        var scenarios = new[]
        {
            new BddScenario(
                FeatureTitle: "Travel — intent routing",
                ScenarioName: "Plan a trip",
                PromptPattern: "plan.*trip",
                ReplyText: "...",
                Tags: new[] { "@neuron:travel.plan-trip" },
                SourceFile: "/abs/path/to/travel-intent.feature"),
        };

        var corpus = new BddScenarioPromptCorpus(scenarios);
        var pattern = corpus.ByNeuron[NeuronId.From("travel.plan-trip")][0];

        Assert.Equal("plan.*trip", pattern.Pattern);
        Assert.Equal("Plan a trip", pattern.ScenarioName);
        Assert.EndsWith("travel-intent.feature", pattern.SourceFile);
    }

    [Fact]
    public void Empty_input_yields_empty_corpus()
    {
        var corpus = new BddScenarioPromptCorpus(Array.Empty<BddScenario>());
        Assert.Equal(0, corpus.Count);
        Assert.Empty(corpus.ByNeuron);
    }

    static BddScenario Scenario(string name, string pattern, IReadOnlyList<string> tags) =>
        new(
            FeatureTitle: "test-feature",
            ScenarioName: name,
            PromptPattern: pattern,
            ReplyText: "reply",
            Tags: tags,
            SourceFile: "test.feature");
}

public class BddScenarioExtensionsTests
{
    [Fact]
    public void TryGetNeuronId_returns_first_neuron_tag()
    {
        var scenario = MakeScenario("@neuron:travel.plan-trip");
        Assert.True(scenario.TryGetNeuronId(out var id));
        Assert.Equal(NeuronId.From("travel.plan-trip"), id);
    }

    [Fact]
    public void TryGetNeuronId_ignores_non_neuron_tags()
    {
        var scenario = MakeScenario("@reactive", "@slow");
        Assert.False(scenario.TryGetNeuronId(out _));
    }

    [Fact]
    public void TryGetNeuronId_picks_first_when_multiple_tags()
    {
        // Pathological — a scenario tagged for two neurons. The corpus
        // takes the first; document the behavior so it's not load-bearing
        // either way.
        var scenario = MakeScenario("@neuron:travel.plan-trip", "@neuron:travel.find-flights");
        Assert.True(scenario.TryGetNeuronId(out var id));
        Assert.Equal(NeuronId.From("travel.plan-trip"), id);
    }

    [Fact]
    public void TryGetNeuronId_rejects_empty_value()
    {
        var scenario = MakeScenario("@neuron:");
        Assert.False(scenario.TryGetNeuronId(out _));
    }

    static BddScenario MakeScenario(params string[] tags) =>
        new(
            FeatureTitle: "f",
            ScenarioName: "s",
            PromptPattern: "p",
            ReplyText: "r",
            Tags: tags,
            SourceFile: "x.feature");
}
