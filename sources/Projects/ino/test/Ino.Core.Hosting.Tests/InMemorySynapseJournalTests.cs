using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public class InMemorySynapseJournalTests
{
    static SynapseJournalEntry Entry(
        long ts = 0, string kind = "SynapseFired", string verb = "FooRequest",
        string target = "Ino.Demo.Foo", string source = "gateway") =>
        new(TimestampUnixMs: ts, Kind: kind, SynapseVerb: verb,
            CorrelationId: "c1", SourceNeuron: source, TargetNeuron: target);

    [Fact]
    public void Recent_returns_most_recent_entries_in_reverse_order()
    {
        var j = new InMemorySynapseJournal();
        for (var i = 0; i < 5; i++) j.Record(Entry(ts: i));

        var recent = j.Recent(null, 3);

        Assert.Equal(3, recent.Count);
        Assert.Equal(new long[] { 4, 3, 2 }, recent.Select(e => e.TimestampUnixMs));
    }

    [Fact]
    public void Recent_filter_by_neuron_matches_both_source_and_target()
    {
        var j = new InMemorySynapseJournal();
        j.Record(Entry(ts: 1, target: "A", source: "gateway"));
        j.Record(Entry(ts: 2, target: "B", source: "A"));
        j.Record(Entry(ts: 3, target: "C", source: "gateway"));

        var hits = j.Recent("A", 10);

        Assert.Equal(2, hits.Count);
        Assert.Equal(new long[] { 2, 1 }, hits.Select(e => e.TimestampUnixMs));
    }

    [Fact]
    public void Ring_buffer_drops_oldest_when_capacity_exceeded_but_keeps_counters()
    {
        var j = new InMemorySynapseJournal();
        var overflow = InMemorySynapseJournal.Capacity + 50;
        for (var i = 0; i < overflow; i++) j.Record(Entry(ts: i, target: "hot"));

        var recent = j.Recent(null, int.MaxValue);
        Assert.Equal(InMemorySynapseJournal.Capacity, recent.Count);

        var metrics = j.Metrics("hot").PerNeuron.Single();
        Assert.Equal(overflow, metrics.FireCount);
    }

    [Fact]
    public void Metrics_splits_fires_and_broadcasts_and_tracks_last_activation()
    {
        var j = new InMemorySynapseJournal();
        j.Record(Entry(ts: 10, kind: "SynapseFired",     target: "x"));
        j.Record(Entry(ts: 20, kind: "SynapseFired",     target: "x"));
        j.Record(Entry(ts: 30, kind: "SynapseBroadcast", target: "x"));

        var m = j.Metrics("x").PerNeuron.Single();
        Assert.Equal(2, m.FireCount);
        Assert.Equal(1, m.BroadcastCount);
        Assert.Equal(30, m.LastActivatedUnixMs);
    }

    [Fact]
    public void Metrics_orders_by_total_activations_descending()
    {
        var j = new InMemorySynapseJournal();
        j.Record(Entry(target: "quiet"));
        j.Record(Entry(target: "busy"));
        j.Record(Entry(target: "busy"));
        j.Record(Entry(target: "busy"));
        j.Record(Entry(target: "medium"));
        j.Record(Entry(target: "medium"));

        var order = j.Metrics(null).PerNeuron.Select(m => m.NeuronId).ToList();

        Assert.Equal(new[] { "busy", "medium", "quiet" }, order);
    }
}
