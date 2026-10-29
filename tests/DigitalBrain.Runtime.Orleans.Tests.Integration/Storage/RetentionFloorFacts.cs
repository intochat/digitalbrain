using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

public sealed class RetentionFloorFacts
{
    private static readonly string P = BrainStore.P.ToString();

    private static BrainStore Store(TempHome home, ManualTime time, FixedCursors cursors, int windowSignals)
        => new(home.Path, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IRetentionPolicy>(new RetentionPolicy(BrainStore.Window(windowSignals), time, cursors));
        });

    // Signals older than the 7-day age window, so the signal-count window decides (D-11 "larger of").
    private static void Age(ManualTime time) => time.Now = time.Now.AddDays(30);

    private static async Task<StoredSlice> Slice(BrainStore store, string publisher, long since = 0)
        => await (await store.Log()).PublisherSignals(publisher, since, int.MaxValue, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(2500, 2000)]
    [InlineData(1500, 1500)]
    public async Task Checkpoint_trims_at_the_policy_floor_and_PublisherSignals_reports_it(long cursor, long floor)
    {
        using var home = new TempHome();
        var time = new ManualTime();
        await using var store = Store(home, time, new FixedCursors(cursor), 1000);
        await store.Publish(BrainStore.P, 3000, batch: 250);
        Age(time);
        Assert.Equal(0, (await Slice(store, P)).Floor);

        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);

        var slice = await Slice(store, P);
        Assert.Equal(floor, slice.Floor);
        Assert.Equal(3000, slice.Last);
        Assert.Equal(floor + 1, slice.Held[0].Sequence);
        Assert.Equal(3000 - floor, slice.Held.Length);
    }

    [Fact]
    public async Task The_floor_survives_reopen_and_replay_of_entries_after_the_snapshot()
    {
        using var home = new TempHome();
        var cursors = new FixedCursors(2500);
        var time = new ManualTime();
        await using var store = Store(home, time, cursors, 1000);
        await store.Publish(BrainStore.P, 3000, batch: 300);
        await store.Publish(BrainStore.Q, 10, batch: 3);
        Age(time);
        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);
        await store.Publish(BrainStore.P, 5, batch: 2);

        await store.Reopen();

        var slice = await Slice(store, P);
        Assert.Equal(2000, slice.Floor);
        Assert.Equal(3005, slice.Last);
        Assert.Equal(Enumerable.Range(2001, 1005).Select(n => (long)n), slice.Held.Select(s => s.Sequence));
        // The next append continues the publisher's numbering.
        await store.Publish(BrainStore.P, 1);
        Assert.Equal(3006, (await Slice(store, P)).Last);
        Assert.Equal(10, (await Slice(store, BrainStore.Q.ToString())).Held.Length);
        // A second checkpoint over the reopened log keeps the contiguity from Floor + 1.
        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);
        await store.Reopen();
        Assert.Equal(2006, (await Slice(store, P)).Floor);
    }

    [Fact]
    public async Task Age_beats_count_when_more_signals_are_newer_than_the_age_window()
    {
        using var home = new TempHome();
        var time = new ManualTime();
        var now = time.Now;
        await using var store = Store(home, time, new FixedCursors(null), 50);
        // 80 signals 30 to 8 days old, then 120 within the last 7 days.
        await store.Publish(BrainStore.P, 200, batch: 1,
            beforeEntry: n => time.Now = n < 80 ? now.AddDays(-30).AddHours(n) : now.AddDays(-6).AddMinutes(n));
        time.Now = now;

        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);

        var slice = await Slice(store, P);
        Assert.Equal(80, slice.Floor);
        Assert.Equal(120, slice.Held.Length);
    }

    [Fact]
    public async Task Count_beats_age_when_every_signal_is_older_than_the_age_window()
    {
        using var home = new TempHome();
        var time = new ManualTime();
        var now = time.Now;
        await using var store = Store(home, time, new FixedCursors(null), 60);
        await store.Publish(BrainStore.P, 100, batch: 7, beforeEntry: n => time.Now = now.AddDays(-20).AddHours(n));
        time.Now = now;

        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);

        var slice = await Slice(store, P);
        Assert.Equal(40, slice.Floor);
        Assert.Equal(Enumerable.Range(41, 60).Select(n => (long)n), slice.Held.Select(s => s.Sequence));
    }

    [Fact]
    public async Task Without_a_registered_policy_the_DefaultRetentionPolicy_applies_the_windows_only()
    {
        using var home = new TempHome();
        var time = new ManualTime();
        await using var store = new BrainStore(home.Path, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.Configure<RetentionOptions>(o => o.HistoryWindowSignals = 1000);
        });
        Assert.IsType<DefaultRetentionPolicy>(store.Services.GetRequiredService<IRetentionPolicy>());
        await store.Publish(BrainStore.P, 3000, batch: 500);
        Age(time);

        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);

        Assert.Equal(2000, (await Slice(store, P)).Floor);
    }

    [Fact]
    public async Task A_policy_floor_never_passes_last_and_never_moves_down()
    {
        using var home = new TempHome();
        var cursors = new FixedCursors(9_999);
        var time = new ManualTime();
        await using var store = Store(home, time, cursors, 0);
        await store.Publish(BrainStore.P, 10, batch: 4);
        Age(time);
        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);
        Assert.Equal(10, (await Slice(store, P)).Floor);
        Assert.Empty((await Slice(store, P)).Held);

        cursors.Lowest = 2;
        await store.Publish(BrainStore.P, 3);
        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);
        await store.Reopen();
        var slice = await Slice(store, P);
        Assert.Equal(10, slice.Floor);
        Assert.Equal([11L, 12L, 13L], slice.Held.Select(s => s.Sequence));
    }
}
