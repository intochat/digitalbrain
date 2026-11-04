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

public interface ISbPayments
{
    Task Charge(decimal amount);

    Task ChargeCapped(decimal amount);

    Task Stall();
}

[Neuron("sb.payments")]
public sealed class SbPayments : Neuron<SbPayState>, ISbPayments
{
    public Task Charge(decimal amount) => Save(State with { Count = State.Count + 1 }, new SbCharging(amount));

    public Task ChargeCapped(decimal amount) => Save(State with { Count = State.Count + 1 }, new SbCappedCharging(amount));

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
