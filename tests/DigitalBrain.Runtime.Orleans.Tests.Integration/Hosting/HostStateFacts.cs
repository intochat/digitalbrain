using DigitalBrain.Neurons;
using System.Collections.Concurrent;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Hosting;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

/// <summary>A local silo on a kept Home with BrainLog as the <c>cluster</c> store and the hosting module composed.</summary>
public sealed class StatefulSilo : IAsyncLifetime
{
    private readonly TempHome _home = new();
    private IInProcessHost? _host;

    public IServiceProvider Services => (_host ?? throw new InvalidOperationException("Not started.")).Services;

    public T Get<T>(string id) where T : class => NeuronHosting.Get<T>(Services, NeuronId.Parse(id));

    public ISignalHistory History(string brain) => Services.GetRequiredService<BrainSignalHistories>().For(NeuronId.Parse(brain + "/x/y").Brain);

    public Task Restart(CancellationToken ct) => _host!.Restart(ct);

    public async ValueTask InitializeAsync()
        => _host = await new LocalSiloHostFactory(new Stores()).Start(new InProcessHostOptions { Home = _home.Path, Modules = [typeof(HostingModule)] }, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) { await _host.DisposeAsync(); }
        _home.Dispose();
    }

    private sealed class Stores : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder silo)
        {
            silo.AddBrainStorage(typeof(HostingModule).Assembly);
            silo.Configure<ReminderOptions>(options => options.MinimumReminderPeriod = TimeSpan.FromSeconds(1));
        }
    }
}

// 04 D-01, 06 D-02, D-14: the host neuron context over BrainLog.
public sealed class HostStateFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task Save_writes_json_state_under_the_class_state_name_with_its_signals_in_one_entry_and_state_reloads_after_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        var counter = silo.Get<ICounter>("b1/probe.counter/save");
        Assert.Equal(2, await counter.Add(2));
        Assert.Equal(5, await counter.Add(3));

        var logs = silo.Services.GetRequiredService<BrainLogs>();
        var record = await (await logs.For("b1")).Read("neuron.host/b1/probe.counter/save", "count");
        Assert.NotNull(record);
        Assert.Equal(5, JournalPayloads.DecodeState<CounterState>(record.Record)!.Value);
        var page = await silo.History("b1").Read(NeuronId.Parse("b1/probe.counter/save"), 0, 10, ct);
        Assert.Equal([2, 5], page.Signals.Select(signal => ((Counted)signal.Signal).Value));
        Assert.Equal([1L, 2L], page.Signals.Select(signal => signal.Sequence));

        await silo.Restart(ct);
        Assert.Equal(5, await silo.Get<ICounter>("b1/probe.counter/save").Value());
    }

    [Fact]
    public async Task Publish_without_state_appends_a_signal_only_entry_then_announces_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var received = new ConcurrentQueue<(NeuronId Pub, Noted Signal)>();
        var clusterListeners = silo.Services.GetRequiredService<IClusterListeners>();
        await using var sub = await clusterListeners.Listen<Noted>("test.watcher", (pub, s, token) =>
        {
            received.Enqueue((pub, s));
            return Task.CompletedTask;
        });
        var counter = silo.Get<ICounter>("b1/probe.counter/note");
        await counter.Note("hi");

        var page = await silo.History("b1").Read(NeuronId.Parse("b1/probe.counter/note"), 0, 10, ct);
        Assert.Equal("hi", ((Noted)Assert.Single(page.Signals).Signal).Text);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (received.IsEmpty && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
        var (itemPub, itemSignal) = Assert.Single(received);
        Assert.Equal(NeuronId.Parse("b1/probe.counter/note"), itemPub);
        Assert.Equal("hi", itemSignal.Text);
        Assert.Equal(0, await counter.Value());
    }

    [Fact]
    public async Task Publish_ephemeral_delivers_to_listeners_without_journal_append()
    {
        var ct = TestContext.Current.CancellationToken;
        var clusterListeners = silo.Services.GetRequiredService<DigitalBrain.Kernel.IClusterListeners>();
        var heard = new ConcurrentQueue<string>();
        await using var reg = await clusterListeners.Listen<EphemeralPing>("test-ephemeral", (_, signal, _) =>
        {
            heard.Enqueue(signal.Message);
            return Task.CompletedTask;
        });

        var counter = silo.Get<ICounter>("b1/probe.counter/ephemeral");
        await counter.PingEphemeral("ping-fast");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (heard.IsEmpty && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }

        Assert.Equal(["ping-fast"], heard);
        var page = await silo.History("b1").Read(NeuronId.Parse("b1/probe.counter/ephemeral"), 0, 10, ct);
        Assert.Empty(page.Signals);
        Assert.Equal(0, page.Last);
    }

    [Fact]
    public async Task A_timer_runs_as_a_turn_and_a_reminder_fires_after_a_silo_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        var counter = silo.Get<ICounter>("b1/probe.counter/timed");
        await counter.Tick("timer", 50);
        await ProbeLog.Until(() => CounterLog.Ticks.ContainsKey("b1/probe.counter/timed:timer"), ct);

        await counter.Remind("once-restart", 8000, 1000);
        await silo.Restart(ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        while (!CounterLog.Ticks.ContainsKey("b1/probe.counter/timed:once-restart")) { await Task.Delay(100, limit.Token); }
    }

    [Fact]
    public async Task Caller_is_the_stamped_neuron_History_is_limited_to_the_brain_and_a_stateless_Save_is_refused()
    {
        var a = silo.Get<ICounter>("b1/probe.counter/caller-a");
        Assert.Equal("", await a.Caller());
        Assert.Equal("b1/probe.counter/caller-a", await a.AskCaller("caller-b"));
        Assert.Equal("read", await a.ReadForeign("b1/probe.counter/caller-b"));
        Assert.Equal("refused", await a.ReadForeign("b2/probe.counter/caller-b"));
        Assert.Equal(0, await a.HistoryLast());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => silo.Get<IStatelessSaver>("b1/probe.stateless.saver/x").Save());
        Assert.Contains("StatelessNeuron", error.Message);
    }
}
