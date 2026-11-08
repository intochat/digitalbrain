using DigitalBrain.Capabilities.Compute;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Signals;

/// <summary>Every journaled signal carries a trace id, and a reaction's signals name the signal that caused them.</summary>
public sealed class TraceStampingFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ISignalHistory History => silo.Services.GetRequiredService<BrainSignalHistories>().For(new BrainId("b1"));

    private async Task<JournaledSignal[]> Journal(string publisher)
        => [.. (await History.Read(NeuronId.Parse(publisher), 0, 100, Ct)).Signals];

    [Fact]
    public async Task RequestSaveTakesTraceContextId()
    {
        const string Source = "b1/probe.counter/trace-request";
        var trace = TraceContext.Begin("acct", "b1", NeuronId.Parse(Source));
        using (TraceContext.Enter(trace)) { await silo.Get<ICounter>(Source).Add(1); }

        var saved = Assert.Single(await Journal(Source));
        Assert.Equal(trace.TraceId, saved.TraceId);
        Assert.Null(saved.CausationId);
    }

    [Fact]
    public async Task ReactionSaveInheritsTraceAndSetsCausation()
    {
        const string Source = "b1/probe.counter/trace-cause", Relay = "b1/probe.relay/trace-cause";
        await silo.Get<IRelay>(Relay).Follow(Source);
        var trace = TraceContext.Begin("acct", "b1", NeuronId.Parse(Source));
        using (TraceContext.Enter(trace))
        {
            for (var n = 0; n < 7; n++) { await silo.Get<ICounter>(Source).Add(1); }
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        JournaledSignal[] relayed;
        while ((relayed = await Journal(Relay)).Length < 7 && DateTime.UtcNow < deadline) { await Task.Delay(50, Ct); }
        var seventh = Assert.Single(relayed, item => item.Signal is Noted { Text: "relayed:7" });
        Assert.Equal(trace.TraceId, seventh.TraceId);
        Assert.Equal($"{Source}:7", seventh.CausationId);
        Assert.Equal(Causation.Address(NeuronId.Parse(Source), 7), seventh.CausationId);
    }

    [Fact]
    public async Task CalledNeuronInheritsTraceAndCausationFromReaction()
    {
        // No request trace: the source save gets a fresh one, which the reaction carries into the neuron it calls.
        const string Source = "b1/probe.counter/trace-call", Caller = "b1/probe.caller/trace-call", Called = "b1/probe.counter/trace-call-called";
        await silo.Get<ICaller>(Caller).Follow(Source);
        await silo.Get<ICounter>(Source).Add(1);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        JournaledSignal[] called;
        while ((called = await Journal(Called)).Length < 1 && DateTime.UtcNow < deadline) { await Task.Delay(50, Ct); }
        var cause = Assert.Single(await Journal(Source));
        var effect = Assert.Single(called);
        Assert.NotNull(cause.TraceId);
        Assert.Equal(cause.TraceId, effect.TraceId);
        Assert.Equal(Causation.Address(NeuronId.Parse(Source), 1), effect.CausationId);
    }

    [Fact]
    public async Task SaveOutsideContextGetsFreshTrace()
    {
        const string Source = "b1/probe.counter/trace-fresh";
        await silo.Get<ICounter>(Source).Add(1);
        await silo.Get<ICounter>(Source).Add(1);

        var saved = await Journal(Source);
        Assert.Equal(2, saved.Length);
        Assert.All(saved, item => Assert.False(string.IsNullOrEmpty(item.TraceId)));
        Assert.NotEqual(saved[0].TraceId, saved[1].TraceId);
        Assert.All(saved, item => Assert.Null(item.CausationId));
    }

    [Fact]
    public async Task ReadTraceReturnsOnlyThatTrace()
    {
        const string Source = "b1/probe.counter/trace-read";
        var trace = TraceContext.Begin("acct", "b1", NeuronId.Parse(Source));
        await silo.Get<ICounter>(Source).Add(1);
        using (TraceContext.Enter(trace)) { await silo.Get<ICounter>(Source).Add(1); }
        await silo.Get<ICounter>(Source).Add(1);

        var page = await History.ReadTrace(trace.TraceId, 0, 100);
        var only = Assert.Single(page.Signals);
        Assert.Equal(trace.TraceId, only.TraceId);
        Assert.Equal(new Counted(2), only.Signal);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => History.ReadTrace(trace.TraceId, 0, 0));
    }
}

public interface IRelay
{
    Task Follow(string source);
}

/// <summary>Reacts to each <see cref="Counted"/> by saving a <see cref="Noted"/> of its own.</summary>
[Neuron("probe.relay", State = "relay")]
public sealed class RelayNeuron : Neuron<CounterState>, IRelay
{
    public Task Follow(string source) => Context.Synapses.Ensure<Counted>(NeuronId.Parse(source));

    [On]
    public Task On(Counted signal) => Save(State with { Value = signal.Value }, new Noted("relayed:" + signal.Value));
}

public interface ICaller
{
    Task Follow(string source);
}

/// <summary>Reacts to each <see cref="Counted"/> by calling the counter keyed <c>{its key}-called</c>, which saves in the reaction's trace.</summary>
[Neuron("probe.caller")]
public sealed class CallerNeuron : Neuron, ICaller
{
    public Task Follow(string source) => Context.Synapses.Ensure<Counted>(NeuronId.Parse(source));

    [On]
    public Task On(Counted signal) => Get<ICounter>(new NeuronId(Id.Brain, "probe.counter", Id.Key + "-called")).Add(signal.Value);
}
