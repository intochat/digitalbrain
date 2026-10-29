using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// 04 D-03, D-04, T-04-38, T-04-40: per-source cursor, dedupe, order, filter, failure retry.
public sealed class SynapseCursorFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    [Fact]
    public async Task References_at_or_below_the_cursor_read_nothing_and_signals_arrive_once_in_sequence_order_even_when_references_arrive_reversed()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/order", Source = "b1/probe.counter/order";
        await silo.Get<IListener>(Listener).Follow(Source);

        silo.Faults.FailAfterAppend = true;
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => silo.Get<ICounter>(Source).Add(1));
            await Assert.ThrowsAnyAsync<Exception>(() => silo.Get<ICounter>(Source).Add(1));
        }
        finally { silo.Faults.FailAfterAppend = false; }
        Assert.Empty(Heard.Of(Listener));

        await silo.Nudge(Source, 2, "probe.counted");
        await silo.Nudge(Source, 1, "probe.counted");
        await silo.Nudge(Source, 2, "probe.counted");
        await Heard.Until(Listener, 2, ct);
        await Task.Delay(500, ct);
        Assert.Equal(2, Heard.Of(Listener).Length);

        var reads = silo.Counters.HistoryReads;
        var dropped = silo.Counters.DuplicatesDropped;
        await silo.Nudge(Source, 1, "probe.counted");
        await silo.Nudge(Source, 2, "probe.counted");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (silo.Counters.DuplicatesDropped < dropped + 2 && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
        Assert.True(silo.Counters.DuplicatesDropped >= dropped + 2);
        Assert.Equal(reads, silo.Counters.HistoryReads);
        Assert.Equal(2, Heard.Of(Listener).Length);
    }

    [Fact]
    public async Task Signals_of_one_source_reach_On_in_sequence_order()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/sequence", Source = "b1/probe.counter/sequence";
        await silo.Get<IListener>(Listener).Follow(Source);
        for (var i = 0; i < 5; i++) { await silo.Get<ICounter>(Source).Add(1); }
        await Heard.Until(Listener, 5, ct);
        Assert.Equal(["counted:1", "counted:2", "counted:3", "counted:4", "counted:5"], Heard.Of(Listener));
    }

    [Fact]
    public async Task The_cursor_is_persisted_in_the_subscriber_state_and_reported_to_the_cursor_registry()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/persist", Source = "b1/probe.counter/persist";
        await silo.Get<IListener>(Listener).Follow(Source);
        await silo.Get<ICounter>(Source).Add(4);
        await silo.Get<ICounter>(Source).Add(4);
        await Heard.Until(Listener, 2, ct);
        await Task.Delay(300, ct);

        var record = await (await silo.Services.GetRequiredService<BrainLogs>().For("b1")).Read("neuron.host/" + Listener, SynapseState.Name);
        Assert.NotNull(record);
        var state = JournalPayloads.DecodeState<SynapseState>(record.Record)!;
        var entry = Assert.Single(state.Sources);
        Assert.Equal(Source, entry.Source);
        Assert.Equal(["probe.counted"], entry.Aliases);
        Assert.Equal(2, entry.Cursor);

        var registry = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<ISynapseCursorRegistry>("b1");
        Assert.DoesNotContain(Listener, await registry.Behind(NeuronId.Parse(Source), 2));
        Assert.Contains(Listener, await registry.Behind(NeuronId.Parse(Source), 3));
    }

    [Fact]
    public async Task Signals_are_filtered_by_type_and_references_of_unknown_sources_are_ignored()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/filter", Source = "b1/probe.counter/filter", Stranger = "b1/probe.counter/stranger";
        await silo.Get<IListener>(Listener).Follow(Source);
        await silo.Get<ICounter>(Source).Note("not followed");
        await silo.Get<ICounter>(Source).Add(9);
        await Heard.Until(Listener, 1, ct);

        await silo.Get<ICounter>(Stranger).Add(1);
        await silo.Nudge(Stranger, 1, "probe.counted");
        await Task.Delay(700, ct);
        Assert.Equal(["counted:9"], Heard.Of(Listener));
    }

    [Fact]
    public async Task A_reaction_that_throws_keeps_the_cursor_before_the_signal_and_the_next_reference_retries_it()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/retry", Source = "b1/probe.counter/retry";
        await silo.Get<IListener>(Listener).FollowNotes(Source);
        var failures = silo.Counters.ReactionFailures;
        await silo.Get<ICounter>(Source).Note("boom");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (silo.Counters.ReactionFailures == failures && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
        Assert.True(silo.Counters.ReactionFailures > failures);
        Assert.Empty(Heard.Of(Listener));

        await silo.Get<ICounter>(Source).Note("after");
        await Heard.Until(Listener, 2, ct);
        Assert.Equal(["noted:boom", "noted:after"], Heard.Of(Listener));
    }
}

// 04 D-04: a subscriber behind the floor receives Gap(source, cursor, floor) first.
public sealed class SynapseGapFacts(ShortRetentionSilo silo) : IClassFixture<ShortRetentionSilo>
{
    [Fact]
    public async Task A_stale_cursor_behind_a_forced_checkpoint_receives_Gap_then_the_signals_after_the_floor()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/gap", Source = "b1/probe.counter/gap";
        await silo.Get<IListener>(Listener).Follow(Source);
        await silo.Get<ICounter>(Source).Add(1);
        await Heard.Until(Listener, 1, ct);
        await Task.Delay(1000, ct); // the delivery that ran counted:1 finishes its page before the publisher writes again

        silo.Faults.FailAfterAppend = true;
        try
        {
            for (var i = 0; i < 5; i++) { await Assert.ThrowsAnyAsync<Exception>(() => silo.Get<ICounter>(Source).Add(1)); }
        }
        finally { silo.Faults.FailAfterAppend = false; }
        await RuntimeTestHooks.ForceCheckpoint(silo.Services, "b1", ct);

        await silo.Nudge(Source, 6, "probe.counted");
        await Heard.Until(Listener, 4, ct);
        var heard = Heard.Of(Listener);
        Assert.Equal(4, heard.Length);
        Assert.Equal("counted:1", heard[0]);
        Assert.Equal("gap:1-4", heard[1]);
        Assert.All(heard[2..], item => Assert.StartsWith("counted:", item, StringComparison.Ordinal));
    }
}
