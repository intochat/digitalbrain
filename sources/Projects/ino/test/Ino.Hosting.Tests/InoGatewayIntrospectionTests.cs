using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Gateway;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Hosting.Tests;

/// <summary>
/// Locks the wire between <see cref="IInoGateway"/> and the underlying
/// <see cref="ISynapseJournal"/> — the introspection RPCs the Flutter
/// inspector drawer (slice 14) and any MCP client reads from.
/// </summary>
public class InoGatewayIntrospectionTests
{
    static InoGateway MakeGateway(ISynapseJournal journal, IReasoningProbe? probe = null) =>
        new(firePort: Substitute.For<IFirePort>(),
            events: Substitute.For<IInoEventBus>(),
            journal: journal,
            reasoningProbe: probe ?? new InMemoryReasoningProbe(),
            grainFactory: Substitute.For<IGrainFactory>(),
            log: NullLogger<InoGateway>.Instance);

    static SynapseJournalEntry Entry(long ts, string target) =>
        new(TimestampUnixMs: ts, Kind: "SynapseFired", SynapseVerb: "Foo",
            CorrelationId: "c", SourceNeuron: "gateway", TargetNeuron: target);

    [Fact]
    public async Task GetJournalAsync_passes_limit_through_to_underlying_journal()
    {
        var journal = new InMemorySynapseJournal();
        for (var i = 0; i < 10; i++) journal.Record(Entry(ts: i, target: "x"));

        var gateway = MakeGateway(journal);

        var page = await gateway.GetJournalAsync(null, limit: 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, page.Count);
        Assert.Equal(9, page[0].TimestampUnixMs);
    }

    [Fact]
    public async Task GetJournalAsync_clamps_zero_and_negative_limits_to_safe_default()
    {
        var journal = new InMemorySynapseJournal();
        for (var i = 0; i < 60; i++) journal.Record(Entry(ts: i, target: "x"));

        var gateway = MakeGateway(journal);

        var page = await gateway.GetJournalAsync(null, limit: 0, TestContext.Current.CancellationToken);
        Assert.Equal(50, page.Count);
    }

    [Fact]
    public async Task GetMetricsAsync_scopes_to_specific_neuron_when_id_provided()
    {
        var journal = new InMemorySynapseJournal();
        journal.Record(Entry(ts: 1, target: "a"));
        journal.Record(Entry(ts: 2, target: "b"));
        journal.Record(Entry(ts: 3, target: "b"));

        var gateway = MakeGateway(journal);

        var scoped = await gateway.GetMetricsAsync("b", TestContext.Current.CancellationToken);
        Assert.Single(scoped.PerNeuron, m => m.NeuronId == "b" && m.FireCount == 2);
    }

    [Fact]
    public async Task GetReasoningAsync_returns_empty_placeholder_when_probe_has_no_entry()
    {
        var gateway = MakeGateway(new InMemorySynapseJournal());

        var reasoning = await gateway.GetReasoningAsync(
            "Ino.Domains.Travel.Neurons.FlightSearchNeuron",
            TestContext.Current.CancellationToken);

        Assert.Equal("Ino.Domains.Travel.Neurons.FlightSearchNeuron", reasoning.NeuronId);
        Assert.Equal("bdd-mock", reasoning.Source);
        Assert.Empty(reasoning.ScenarioName);
    }

    [Fact]
    public async Task GetReasoningAsync_returns_probe_hit_when_scenario_has_matched()
    {
        var probe = new InMemoryReasoningProbe();
        probe.Record("FlightSearchNeuron", new ReasoningRecord(
            Source: "bdd-mock",
            ScenarioName: "Find flights",
            FeatureTitle: "Travel — intent routing",
            Prompt: "find flights to Bali",
            Reply: "Searching flights via the FlightSearch neuron.",
            Timestamp: DateTimeOffset.UtcNow));
        var gateway = MakeGateway(new InMemorySynapseJournal(), probe);

        var reasoning = await gateway.GetReasoningAsync(
            "FlightSearchNeuron",
            TestContext.Current.CancellationToken);

        Assert.Equal("bdd-mock", reasoning.Source);
        Assert.Equal("Find flights", reasoning.ScenarioName);
        Assert.Contains("mocked via BDD", reasoning.Text);
        Assert.Contains("Find flights", reasoning.Text);
    }
}
