using System.Collections.Concurrent;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

[Signal("test.sb.charging")]
public sealed record SbCharging(decimal Amount) : Signal;

[Signal("test.sb.stalling")]
public sealed record SbStalling : Signal;

[State("sb.week")]
public sealed record SbWeekSpend(decimal Spent)
{
    public SbWeekSpend() : this(0m) { }
}

[State("sb.ledger")]
public sealed record SbLedger(decimal Spent)
{
    public SbLedger() : this(0m) { }
}

[State("sb.hold")]
public sealed record SbHold(int Seen)
{
    public SbHold() : this(0) { }
}

public sealed record SbPayState
{
    public int Count { get; init; }
}

public static class SbBudget
{
    [Synapse(typeof(SbCharging), "sb.week")]
    public static Check WeeklyCap(SbCharging s, SbWeekSpend state) =>
        state.Spent + s.Amount > 300m
            ? Check.Reject("weekly cap of $300")
            : Check.Allow(state with { Spent = state.Spent + s.Amount });

    [Synapse("sb.week")]
    public static string Show(SbWeekSpend w) => $"{w.Spent} of 300";
}

public static class SbBooks
{
    [Synapse(typeof(SbCharging), "sb.ledger")]
    public static SbLedger Add(SbCharging s, SbLedger w) => w with { Spent = w.Spent + s.Amount };
}

// A gate that never answers on its own: it waits for a completion nobody sets, until its call is cancelled.
public static class SbStuck
{
    private static readonly TaskCompletionSource Never = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Synapse(typeof(SbStalling), "sb.hold")]
    public static async Task<Check> Wait(SbStalling s, SbHold state, CancellationToken ct)
    {
        await Never.Task.WaitAsync(ct);
        return Check.Allow(state with { Seen = state.Seen + 1 });
    }
}

[Signal("test.sb.capped-charging")]
public sealed record SbCappedCharging(decimal Amount) : Signal;

[State("sb.cap")]
public sealed record SbCapSpend(decimal Spent)
{
    public SbCapSpend() : this(0m) { }
}

public static class SbCapping
{
    [Synapse(typeof(SbCappedCharging), "sb.cap")]
    public static Check Hundred(SbCappedCharging s, SbCapSpend state) =>
        state.Spent + s.Amount > 100m ? Check.Reject("cap of $100") : Check.Allow(state with { Spent = state.Spent + s.Amount });
}

[Signal("test.sb.relay")]
public sealed record SbRelay(int N) : Signal;

[Signal("test.sb.forwarded")]
public sealed record SbForwarded(int N) : Signal;

[State("sb.relay")]
public sealed record SbRelayState(int Count)
{
    public SbRelayState() : this(0) { }
}

[State("sb.tally")]
public sealed record SbTally(int Seen)
{
    public SbTally() : this(0) { }
}

// The relay is a state neuron that is itself a publisher: its reaction publishes SbForwarded, which the tally gates. The tally's
// Committed saves StateChanged, and the relay reacts to StateChanged, so ensuring that edge would call the relay while it is
// blocked in its own Commit.
public static class SbRelaying
{
    [Synapse(typeof(SbRelay), "sb.relay")]
    public static Reaction<SbRelayState> Forward(SbRelay s, SbRelayState state) => Reaction<SbRelayState>.Of(state with { Count = state.Count + 1 }, new SbForwarded(s.N));

    [Synapse(typeof(StateChanged), "sb.relay")]
    public static SbRelayState Hear(StateChanged s, SbRelayState state) => state;

    [Synapse(typeof(SbForwarded), "sb.tally")]
    public static Check Count(SbForwarded s, SbTally state) => Check.Allow(state with { Seen = state.Seen + 1 });
}

[Signal("test.sb.voiding")]
public sealed record SbVoiding : Signal;

[State("sb.void")]
public sealed record SbVoid(int Seen)
{
    public SbVoid() : this(0) { }
}

[State("sb.skip")]
public sealed record SbSkip(int Seen)
{
    public SbSkip() : this(0) { }
}

public static class SbOddGates
{
    // A gate that allows without a next state: the publisher fails it closed.
    [Synapse(typeof(SbVoiding), "sb.void")]
    public static Check Empty(SbVoiding s, SbVoid state) => Check.Allow<SbVoid>(null!);

    // A gate on StateChanged: the hosts never call it.
    [Synapse(typeof(StateChanged), "sb.skip")]
    public static Check Never(StateChanged s, SbSkip state) => Check.Reject("never runs");
}

public interface ISbPayments
{
    Task Charge(decimal amount);

    Task Void();

    Task Relay(int n);

    Task ChargeCapped(decimal amount);

    Task Stall();
}

[Neuron("sb.payments")]
public sealed class SbPayments : Neuron<SbPayState>, ISbPayments
{
    public Task Charge(decimal amount) => Save(State with { Count = State.Count + 1 }, new SbCharging(amount));

    public Task ChargeCapped(decimal amount) => Save(State with { Count = State.Count + 1 }, new SbCappedCharging(amount));

    public Task Relay(int n) => Save(State with { Count = State.Count + 1 }, new SbRelay(n));

    public Task Void() => Save(State with { Count = State.Count + 1 }, new SbVoiding());

    public Task Stall() => Save(State with { Count = State.Count + 1 }, new SbStalling());
}

/// <summary><see cref="StatefulSilo"/> with a one-second gate timeout, for the gate that never answers and the ledger reaction.</summary>
public sealed class ShortGateSilo : IAsyncLifetime
{
    private readonly TempHome _home = new();
    private IInProcessHost? _host;

    public IServiceProvider Services => (_host ?? throw new InvalidOperationException("Not started.")).Services;

    public T Get<T>(string id) where T : class => NeuronHosting.Get<T>(Services, NeuronId.Parse(id));

    public ISignalHistory History(string brain) => Services.GetRequiredService<BrainSignalHistories>().For(NeuronId.Parse(brain + "/x/y").Brain);

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
            silo.Services.AddSingleton(RuntimeOptions.Default with { GateTimeout = TimeSpan.FromSeconds(1) });
        }
    }
}

// The StateNeuronOnTestOSFacts scenarios on the silo: gates run before the append, reactions are ensured and delivered once.
public sealed class StateNeuronSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task ASecondChargeOverTheCapIsRejectedBeforeItCommits()
    {
        var ct = TestContext.Current.CancellationToken;
        var pay = NeuronId.Parse("sb/sb.payments/cap");
        var week = NeuronId.Parse("sb/sb.week/default");
        var payments = silo.Get<ISbPayments>(pay.ToString());
        await payments.Charge(250m);

        var e = await Assert.ThrowsAsync<RuleViolationException>(() => payments.Charge(100m));

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
        Assert.Single((await silo.History("sb").Read(pay, 0, 10, ct)).Signals);
        var journal = (await silo.History("sb").Read(week, 0, 10, ct)).Signals;
        Assert.Contains(journal, s => s.Signal is StateChanged);
        Assert.Contains(journal, s => s.Signal is Rejected);
    }

    [Fact]
    public async Task TwoConcurrentChargesAgainstTheCapAllowExactlyOne()
    {
        var first = silo.Get<ISbPayments>("sb/sb.payments/one");
        var second = silo.Get<ISbPayments>("sb/sb.payments/two");

        var charges = new[] { first.ChargeCapped(60m), second.ChargeCapped(60m) };
        await Assert.ThrowsAsync<RuleViolationException>(() => Task.WhenAll(charges));

        Assert.Equal(1, charges.Count(c => c.IsCompletedSuccessfully));
        Assert.Equal(60m, await SpentOf(silo.Services, NeuronId.Parse("sb/sb.cap/default")));
    }

    [Fact]
    public async Task TwoConcurrentAllowedChargesCountBothWhateverOrderTheyCommitIn()
    {
        for (var i = 0; i < 5; i++)
        {
            var brain = $"sbo{i}";
            var first = silo.Get<ISbPayments>($"{brain}/sb.payments/one");
            var second = silo.Get<ISbPayments>($"{brain}/sb.payments/two");

            await Task.WhenAll(first.ChargeCapped(40m), second.ChargeCapped(40m));

            Assert.Equal(80m, await SpentOf(silo.Services, NeuronId.Parse($"{brain}/sb.cap/default")));
        }
    }

    [Fact]
    public async Task AFirstCommitHeldUntilTheSecondCommittedFlushesBothOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        RuntimeTestHooks.BeforeGateCommit(silo.Services, async (gate, _) =>
        {
            if (gate.Brain.Value != "sbhold" || Interlocked.Increment(ref calls) != 1) { return; }
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            var first = silo.Get<ISbPayments>("sbhold/sb.payments/one");
            var second = silo.Get<ISbPayments>("sbhold/sb.payments/two");

            var held = first.ChargeCapped(40m);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await second.ChargeCapped(40m);
            release.TrySetResult();
            await held;

            var cap = NeuronId.Parse("sbhold/sb.cap/default");
            Assert.Equal(80m, await SpentOf(silo.Services, cap));
            Assert.Single((await silo.History("sbhold").Read(cap, 0, 10, ct)).Signals, s => s.Signal is StateChanged);
        }
        finally
        {
            release.TrySetResult();
            RuntimeTestHooks.BeforeGateCommit(silo.Services, null);
        }
    }

    [Fact]
    public async Task AStateNeuronThatPublishesThroughAGateCompletesWithoutStalling()
    {
        var ct = TestContext.Current.CancellationToken;
        var tally = NeuronId.Parse("sb/sb.tally/default");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await silo.Get<ISbPayments>("sb/sb.payments/relay").Relay(1);
        await Until(async () => await SeenOf(tally) == 1, ct);

        Assert.Equal(1, await SeenOf(tally));
        Assert.True(clock.Elapsed < RuntimeOptions.Default.GateTimeout, $"took {clock.Elapsed}");
        Assert.Single((await silo.History("sb").Read(NeuronId.Parse("sb/sb.relay/default"), 0, 10, ct)).Signals, s => s.Signal is SbForwarded);
    }

    private async Task<int> SeenOf(NeuronId id)
    {
        var host = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, id.ToString()));
        var state = await host.InspectState(TestContext.Current.CancellationToken);
        return state is { } json && json.TryGetProperty("seen", out var seen) ? seen.GetInt32() : 0;
    }

    [Fact]
    public async Task AGateOnStateChangedIsNeverCalled()
    {
        var ct = TestContext.Current.CancellationToken;
        var started = new ConcurrentQueue<SynapseStarted>();
        await using var listener = await silo.Services.GetRequiredService<IClusterListeners>().Listen<SynapseStarted>("started", (_, s, _) =>
        {
            started.Enqueue(s);
            return Task.CompletedTask;
        });

        // The week gate allows and its Committed saves StateChanged, which the sb.skip gate is declared on.
        await silo.Get<ISbPayments>("sb/sb.payments/skip").Charge(1m);
        await Until(() => Task.FromResult(started.Any(s => s.Function.EndsWith(".WeeklyCap", StringComparison.Ordinal))), ct);
        await Task.Delay(300, ct);

        Assert.Contains(started, s => s.Function.EndsWith(".WeeklyCap", StringComparison.Ordinal));
        Assert.DoesNotContain(started, s => s.Function.EndsWith(".Never", StringComparison.Ordinal));
        Assert.Empty((await silo.History("sb").Read(NeuronId.Parse("sb/sb.skip/default"), 0, 10, ct)).Signals);
    }

    [Fact]
    public async Task AnAllowanceWithoutANextStateFailsClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var pay = NeuronId.Parse("sb/sb.payments/void");

        var e = await Assert.ThrowsAsync<RuleViolationException>(() => silo.Get<ISbPayments>(pay.ToString()).Void());

        Assert.Equal("the gate did not answer", e.Rejected.Messages[0]);
        Assert.EndsWith(".Empty", e.Rejected.Rules[0], StringComparison.Ordinal);
        Assert.Empty((await silo.History("sb").Read(pay, 0, 10, ct)).Signals);
        Assert.Empty((await silo.History("sb").Read(NeuronId.Parse("sb/sb.void/default"), 0, 10, ct)).Signals);
    }

    internal static async Task<decimal> SpentOf(IServiceProvider services, NeuronId id)
    {
        var host = services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, id.ToString()));
        var state = await host.InspectState(TestContext.Current.CancellationToken);
        return state!.Value.GetProperty("spent").GetDecimal();
    }

    [Fact]
    public async Task ViewChangedIsHeardAfterAnAllowedCharge()
    {
        var ct = TestContext.Current.CancellationToken;
        var heard = new ConcurrentQueue<string>();
        await using var listener = await silo.Services.GetRequiredService<IClusterListeners>().Listen<ViewChanged>("views", (_, changed, _) =>
        {
            heard.Enqueue(changed.Neuron);
            return Task.CompletedTask;
        });

        await silo.Get<ISbPayments>("sb/sb.payments/view").Charge(1m);
        await Until(() => Task.FromResult(heard.Contains("sb/sb.week/default")), ct);

        Assert.Contains("sb/sb.week/default", heard);
    }

    internal static async Task Until(Func<Task<bool>> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition() && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
    }
}

// On its own silo: every Charging of the shared fixture would reach the one ledger.
public sealed class StateNeuronGateTimeoutFacts(ShortGateSilo silo) : IClassFixture<ShortGateSilo>
{
    [Fact]
    public async Task AReactionSynapseIsEnsuredAndDeliversOnceAcrossADeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var pay = NeuronId.Parse("sb/sb.payments/books");
        var books = NeuronId.Parse("sb/sb.ledger/default");

        await silo.Get<ISbPayments>(pay.ToString()).Charge(5m);
        await StateNeuronSiloFacts.Until(async () => await Spent(books) == 5m, ct);
        Assert.Equal(5m, await Spent(books));

        await silo.Services.GetRequiredService<IGrainFactory>().GetGrain<IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero, ct);
        await Task.Delay(500, ct);

        Assert.Equal(5m, await Spent(books));
        Assert.Single((await silo.History("sb").Read(books, 0, 10, ct)).Signals, s => s.Signal is StateChanged);
    }

    [Fact]
    public async Task AGateThatNeverAnswersFailsClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var pay = NeuronId.Parse("sb/sb.payments/stall");

        var e = await Assert.ThrowsAsync<RuleViolationException>(() => silo.Get<ISbPayments>(pay.ToString()).Stall());

        Assert.Equal("the gate did not answer", e.Rejected.Messages[0]);
        Assert.Empty((await silo.History("sb").Read(pay, 0, 10, ct)).Signals);
        Assert.Empty((await silo.History("sb").Read(NeuronId.Parse("sb/sb.hold/default"), 0, 10, ct)).Signals);
    }

    private async Task<decimal> Spent(NeuronId id)
    {
        var host = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, id.ToString()));
        var state = await host.InspectState(TestContext.Current.CancellationToken);
        return state!.Value.GetProperty("spent").GetDecimal();
    }
}
