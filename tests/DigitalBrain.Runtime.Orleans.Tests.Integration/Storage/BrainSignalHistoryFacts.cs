using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Storage;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

public sealed class BrainSignalHistoryFacts
{
    private static BrainStore Store(TempHome home, FixedCursors cursors, int window = 1000, ManualTime? time = null)
        => new(home.Path, services =>
        {
            time ??= new ManualTime();
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IRetentionPolicy>(new RetentionPolicy(BrainStore.Window(window), time, cursors));
        });

    [Fact]
    public async Task Read_by_publisher_returns_pages_ordered_by_sequence_with_floor_and_last()
    {
        using var home = new TempHome();
        await using var store = Store(home, new FixedCursors(null));
        await store.Publish(BrainStore.P, 25, batch: 4);
        await store.Publish(BrainStore.Q, 3);
        var history = store.History();

        var first = await history.Read(BrainStore.P, 0, 10, TestContext.Current.CancellationToken);
        var second = await history.Read(BrainStore.P, first.Signals[^1].Sequence, 10, TestContext.Current.CancellationToken);

        Assert.Equal((0L, 25L), (first.Floor, first.Last));
        Assert.Equal(Enumerable.Range(1, 10).Select(n => (long)n), first.Signals.Select(s => s.Sequence));
        Assert.Equal(Enumerable.Range(11, 10).Select(n => (long)n), second.Signals.Select(s => s.Sequence));
        Assert.All(first.Signals, s => Assert.Equal(BrainStore.P, s.Publisher));
        Assert.Equal(new Count(11), second.Signals[0].Signal);
    }

    [Fact]
    public async Task Read_over_the_whole_brain_orders_by_brain_position_across_publishers()
    {
        using var home = new TempHome();
        await using var store = Store(home, new FixedCursors(null));
        await store.Publish(BrainStore.P, 2);
        await store.Publish(BrainStore.Q, 2);
        await store.Publish(BrainStore.P, 1);

        var page = await store.History().Read(0, 100, TestContext.Current.CancellationToken);

        Assert.Equal([1L, 2L, 3L, 4L, 5L], page.Signals.Select(s => s.Sequence));
        Assert.Equal([BrainStore.P, BrainStore.P, BrainStore.Q, BrainStore.Q, BrainStore.P], page.Signals.Select(s => s.Publisher));
        Assert.Equal((0L, 5L), (page.Floor, page.Last));
        Assert.Equal([4L, 5L], (await store.History().Read(3, 100, TestContext.Current.CancellationToken)).Signals.Select(s => s.Sequence));
    }

    [Fact]
    public async Task A_since_below_the_floor_returns_a_floor_above_since_so_the_caller_can_emit_Gap()
    {
        using var home = new TempHome();
        var time = new ManualTime();
        await using var store = Store(home, new FixedCursors(null), window: 5, time: time);
        await store.Publish(BrainStore.P, 20, batch: 3);
        time.Now = time.Now.AddDays(30); // older than the age window, so the 5-signal window decides
        await (await store.Log()).Checkpoint(TestContext.Current.CancellationToken);

        var page = await store.History().Read(BrainStore.P, 3, 100, TestContext.Current.CancellationToken);
        Assert.Equal(15, page.Floor);
        Assert.True(page.Floor > 3);
        Assert.Equal(16, page.Signals[0].Sequence);
        var brain = await store.History().Read(3, 100, TestContext.Current.CancellationToken);
        Assert.Equal(15, brain.Floor);
        Assert.Equal(16, brain.Signals[0].Sequence);
    }

    [Fact]
    public async Task Read_refuses_a_publisher_of_another_brain_and_an_out_of_range_limit()
    {
        using var home = new TempHome();
        await using var store = Store(home, new FixedCursors(null));
        await store.Publish(BrainStore.P, 1);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()
            => store.History("b2").Read(BrainStore.P, 0, 10, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()
            => store.History().Read(BrainStore.P, 0, 0, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()
            => store.History().Read(0, SignalPage.MaxLimit + 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BrainStorageRegistration_through_the_host_bound_seam_makes_BrainLog_the_cluster_store_on_a_silo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var registration = new WithRetention(new BrainStorageRegistration(BrainStore.TestAssembly));
        await using (var host = await new LocalSiloHostFactory(registration).Start(new InProcessHostOptions { Home = home.Path }, ct))
        {
            Assert.IsType<BrainGrainStorage>(host.Services.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage));
            Assert.NotNull(host.Services.GetRequiredService<BrainSignalHistories>());
            await host.Services.GetRequiredService<IGrainFactory>().GetGrain<ISynapseCursorRegistry>("b1").ReportCursor("s", BrainStore.P, 5);
        }
        await using (var host = await new LocalSiloHostFactory(registration).Start(new InProcessHostOptions { Home = home.Path }, ct))
        {
            // The registry state was written into the b1 journal (JSON) and reads back after a restart.
            Assert.Equal(5, await host.Services.GetRequiredService<IGrainFactory>().GetGrain<ISynapseCursorRegistry>("b1").LowestLive(BrainStore.P));
            // The policy runs outside the log gate, so a checkpoint that asks the registry (stored in this same log) does not deadlock.
            var log = await host.Services.GetRequiredService<BrainLogs>().For("b1");
            await log.AppendEntry("x", "", null, BrainRecordChange.None, [], BrainStore.P.ToString(), [new Count(1)], ct);
            await log.Checkpoint(ct).WaitAsync(TimeSpan.FromSeconds(30), ct);
            Assert.Equal(1, (await log.PublisherSignals(BrainStore.P.ToString(), 0, 10, ct)).Last);
        }
    }

    private sealed class WithRetention(IClusterStoreRegistration inner) : IClusterStoreRegistration
    {
        public void Register(OrleansSdk.Hosting.ISiloBuilder silo)
        {
            inner.Register(silo);
            silo.AddSynapseRetention();
        }
    }

    [Fact]
    public async Task Mixed_adapter_and_runtime_entries_read_as_one_history_and_unknown_types_are_skipped_without_error()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = Store(home, new FixedCursors(null));
        var writer = store.Services.GetRequiredService<IBrainJournalWriter>();
        await store.Publish(BrainStore.P, 1);
        await writer.Append("b1", new JournalStateWrite("legacy.chat/b1/p", "state", JournalStateChange.Write, null, new byte[] { 0xFF }, "Os.State"),
            BrainStore.P.ToString(), [new("os.chat.posted", "{}"u8.ToArray()), new("test.note", """{"text":"from os"}"""u8.ToArray())], ct);

        var page = await store.History().Read(BrainStore.P, 0, 10, ct);
        var brain = await store.History().Read(0, 10, ct);
        var engine = await store.History().Read(BrainStore.P, 0, 10, keepUnknown: true, ct);

        Assert.Equal(3, page.Last);
        Assert.Equal([1L, 3L], page.Signals.Select(s => s.Sequence));
        Assert.Equal([new Count(1), new Note("from os")], page.Signals.Select(s => s.Signal));
        Assert.Equal([1L, 3L], brain.Signals.Select(s => s.Sequence));
        Assert.Equal(new UnknownSignal("os.chat.posted"), engine.Signals[1].Signal);
    }
}
