using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Signalling;

/// <summary>A synapse silo whose sessions and listeners check SessionTtl every 200 ms.</summary>
public class SessionSilo : SynapseSilo
{
    protected override IReadOnlyDictionary<string, string?> Configuration { get; } = new Dictionary<string, string?>
    {
        [IntoChatOSNames.Signalling.Section + ":SessionSweepEvery"] = "00:00:00.200",
    };
}

/// <summary>Its own silo: facts that move the runtime clock past SessionTtl.</summary>
public sealed class SessionTtlSilo : SessionSilo;

internal static class Sessions
{
    public static ISessionSynapses Session(this SynapseSilo silo, string key) => silo.Get<ISessionSynapses>("b1/runtime.session/" + key);

    public static int Value(this SessionSignal signal) => signal.Payload.GetProperty("value").GetInt32();

    /// <summary>Acks and collects until <paramref name="count"/> signals arrived; returns the batches.</summary>
    public static async Task<List<SessionBatch>> Collect(this ISessionSynapses session, int count, CancellationToken ct)
    {
        var batches = new List<SessionBatch>();
        long acked = 0;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        while (batches.Sum(batch => batch.Signals.Count) < count)
        {
            if (await session.Next(acked, limit.Token) is not { } batch) { continue; }
            batches.Add(batch);
            acked = batch.Number;
        }
        return batches;
    }

    public static async Task Until(Func<Task<bool>> condition, CancellationToken ct, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!await condition() && DateTime.UtcNow < deadline) { await Task.Delay(100, ct); }
    }
}

// 04 D-02, D-13, R4-03, T-04-41, T-04-42: the per-session synapse neuron.
public sealed class SessionSynapseFacts(SessionSilo silo) : IClassFixture<SessionSilo>
{
    [Fact]
    public async Task EndDoesNotWaitForAdmissionAndLateApprovalCannotRecreateTheSubscription()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = silo.Session("held-admission");
        await session.Subscribe(NeuronId.Parse("b1/probe.counter/before-held-admission"), ["probe.counted"]);
        using var pollingStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var polling = session.Next(0, pollingStop.Token);
        var subscribing = session.Subscribe(NeuronId.Parse("b1/probe.held-admission/source"), ["probe.counted"]);
        await HeldAdmissionNeuron.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var ending = session.End();
        try
        {
            await ending.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(HeldAdmissionNeuron.Release.Task.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => subscribing.WaitAsync(TimeSpan.FromSeconds(5), ct));
            Assert.Null(await polling.WaitAsync(TimeSpan.FromSeconds(5), ct));
            await session.Subscribe(NeuronId.Parse("b1/probe.counter/after-held-admission"), ["probe.counted"])
                .WaitAsync(TimeSpan.FromSeconds(5), ct);
            await session.End().WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(HeldAdmissionNeuron.Release.Task.IsCompleted);
        }
        finally { HeldAdmissionNeuron.Release.TrySetResult(); await pollingStop.CancelAsync(); }
        var admissionFailure = await Record.ExceptionAsync(() => subscribing.WaitAsync(TimeSpan.FromSeconds(5), ct));
        await ending.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.IsType<InvalidOperationException>(admissionFailure);
        Assert.Equal(0, await silo.Subscriptions("b1/runtime.session/held-admission", "probe.counted"));
    }

    [Fact]
    public async Task Subscribe_makes_one_engine_synapse_owned_by_the_session_and_Next_hands_signals_out_in_source_order()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Source = "b1/probe.counter/session-order";
        var session = silo.Session("order");
        await session.Subscribe(NeuronId.Parse(Source), ["probe.counted"]);
        Assert.Equal(1, await silo.Subscriptions("b1/runtime.session/order", "probe.counted"));

        var counter = silo.Get<ICounter>(Source);
        for (var i = 0; i < 3; i++) { await counter.Add(1); }
        var batches = await session.Collect(3, ct);

        Assert.Equal([1, 2, 3], batches.SelectMany(batch => batch.Signals).Select(signal => signal.Value()));
        Assert.All(batches.SelectMany(batch => batch.Signals), signal => Assert.Equal(Source, signal.Source));
        Assert.All(batches.SelectMany(batch => batch.Signals), signal => Assert.Equal("probe.counted", signal.Alias));
        Assert.Equal(Enumerable.Range(1, batches.Count).Select(n => (long)n), batches.Select(batch => batch.Number));
    }

    [Fact]
    public async Task A_batch_holds_at_most_256_signals_and_is_sent_again_until_acked()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Source = "b1/probe.counter/session-caps";
        var session = silo.Session("caps");
        await session.Subscribe(NeuronId.Parse(Source), ["probe.counted"]);
        var counter = silo.Get<ICounter>(Source);
        for (var i = 0; i < 300; i++) { await counter.Add(1); }
        await Task.Delay(TimeSpan.FromSeconds(3), ct); // let delivery queue all 300

        var first = await session.Next(0, ct);
        Assert.NotNull(first);
        Assert.Equal(1, first.Number);
        Assert.Equal(256, first.Signals.Count);
        var again = await session.Next(0, ct); // not acknowledged: the same batch again
        Assert.NotNull(again);
        Assert.Equal(1, again.Number);
        Assert.Equal(first.Signals.Select(s => s.Sequence), again.Signals.Select(s => s.Sequence));

        var second = await session.Next(1, ct);
        Assert.NotNull(second);
        Assert.Equal(2, second.Number);
        Assert.Equal(44, second.Signals.Count);
        Assert.Equal(Enumerable.Range(1, 300), first.Signals.Concat(second.Signals).Select(signal => signal.Value()));
    }

    [Fact]
    public async Task A_waiting_Next_completes_on_a_later_save_and_does_not_block_Subscribe()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Source = "b1/probe.counter/session-poll";
        var session = silo.Session("poll");
        await session.Subscribe(NeuronId.Parse(Source), ["probe.counted"]);

        var waiting = session.Next(0, ct);
        await Task.Delay(300, ct);
        Assert.False(waiting.IsCompleted);

        // Subscribe while the poll waits: it completes without waiting for the poll ([Interleaves]).
        var subscribe = session.Subscribe(NeuronId.Parse("b1/probe.counter/session-poll-other"), ["probe.counted"]);
        Assert.Same(subscribe, await Task.WhenAny(subscribe, Task.Delay(TimeSpan.FromSeconds(5), ct)));
        await subscribe;
        Assert.False(waiting.IsCompleted);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);
        Assert.False(waiting.IsCompleted);
        await silo.Get<ICounter>(Source).Add(7);
        Assert.Same(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(5), ct)));
        var batch = await waiting;
        Assert.NotNull(batch);
        Assert.Equal([7], batch.Signals.Select(signal => signal.Value()));
    }

    [Fact]
    public async Task End_removes_everything_and_a_session_over_256_synapses_refuses_Subscribe_with_a_typed_error()
    {
        const string Key = "cap";
        var session = silo.Session(Key);
        for (var i = 0; i < 256; i++) { await session.Subscribe(NeuronId.Parse($"b1/probe.counter/session-cap-{i}"), ["probe.counted"]); }
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => session.Subscribe(NeuronId.Parse("b1/probe.counter/session-cap-256"), ["probe.counted"]));
        Assert.Contains("256", error.Message);
        Assert.Equal(1, await silo.Subscriptions("b1/runtime.session/" + Key, "probe.counted"));

        await session.End();
        Assert.Equal(0, await silo.Subscriptions("b1/runtime.session/" + Key, "probe.counted"));
        var registry = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<ISynapseCursorRegistry>("b1");
        Assert.DoesNotContain("b1/runtime.session/" + Key, await registry.Behind(NeuronId.Parse("b1/probe.counter/session-cap-0"), long.MaxValue));
    }

    [Fact]
    public async Task Subscribe_runs_the_source_AdmitSynapse_under_the_session_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Source = "b1/probe.guarded/session-guard";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => silo.Session("refused").Subscribe(NeuronId.Parse(Source), ["probe.counted"]));
        Assert.Equal(0, await silo.Subscriptions("b1/runtime.session/refused", "probe.counted"));

        var admitted = silo.Session("ok-admitted");
        await admitted.Subscribe(NeuronId.Parse(Source), ["probe.counted"]);
        await silo.Get<IGuarded>(Source).Add(5);
        var batches = await admitted.Collect(1, ct);
        Assert.Equal([5], batches.SelectMany(batch => batch.Signals).Select(signal => signal.Value()));
    }
}

[Neuron("probe.held-admission")]
public sealed class HeldAdmissionNeuron : Neuron
{
    internal static TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task AdmitSynapse(NeuronId subscriber, IReadOnlyList<Type> signals)
    {
        await base.AdmitSynapse(subscriber, signals);
        Entered.TrySetResult();
        await Release.Task;
    }
}

// 04 D-13, RESEARCH Pitfall 3: SessionTtl after the last ack removes bindings and cursors.
public sealed class SessionTtlFacts(SessionTtlSilo silo) : IClassFixture<SessionTtlSilo>
{
    [Fact]
    public async Task With_no_ack_for_SessionTtl_the_session_removes_its_synapses_and_cursors()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Session = "b1/runtime.session/ttl", Source = "b1/probe.counter/session-ttl";
        var session = silo.Session("ttl");
        await session.Subscribe(NeuronId.Parse(Source), ["probe.counted"]);
        var registry = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<ISynapseCursorRegistry>("b1");
        await silo.Get<ICounter>(Source).Add(1); // the session's cursor now sits below the publisher's last until acked
        Assert.Contains(Session, await registry.Behind(NeuronId.Parse(Source), long.MaxValue));

        RuntimeTestHooks.Advance(silo.Services, TimeSpan.FromMinutes(9)); // inside SessionTtl: kept
        await Task.Delay(1000, ct);
        Assert.Equal(1, await silo.Subscriptions(Session, "probe.counted"));

        RuntimeTestHooks.Advance(silo.Services, TimeSpan.FromMinutes(2)); // 11 minutes after the last ack
        await Sessions.Until(async () => await silo.Subscriptions(Session, "probe.counted") == 0, ct);
        Assert.Equal(0, await silo.Subscriptions(Session, "probe.counted"));
        Assert.DoesNotContain(Session, await registry.Behind(NeuronId.Parse(Source), long.MaxValue));
    }
}

/// <summary>A session silo whose calls time out after 3 seconds.</summary>
public sealed class ShortResponseSilo : SessionSilo
{
    protected override Action<IServiceCollection>? ConfigureServices
        => services => services.Configure<OrleansSdk.Configuration.SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(3));
}

// An idle long-poll answers before its caller's response timeout, so its answer is never dropped as expired and a batch cut into
// it reaches the caller on that poll (it waited NextTimeout, which equalled the default response timeout).
public sealed class SessionNextTimeoutFacts(ShortResponseSilo silo) : IClassFixture<ShortResponseSilo>
{
    [Fact]
    public async Task An_idle_Next_returns_null_before_the_silos_response_timeout()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = silo.Session("idle");
        await session.Subscribe(NeuronId.Parse("b1/probe.counter/session-idle"), ["probe.counted"]);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var batch = await session.Next(0, ct);

        Assert.Null(batch);
        Assert.InRange(started.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.9));
    }
}
