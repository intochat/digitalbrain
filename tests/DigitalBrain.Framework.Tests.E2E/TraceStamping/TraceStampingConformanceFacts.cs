using DigitalBrain;
using DigitalBrain.Capabilities.Compute;
using DigitalBrain.Testing;

namespace DigitalBrain.Framework.Tests.E2E.TraceStamping;

public sealed class TraceStampingFixture : BothRuntimesFixture
{
    protected override IReadOnlyList<Type> Modules => [typeof(TraceStampingModule)];
}

/// <summary>Trace and causation ids are stamped by the same rule on TestOS and on the silo.</summary>
public sealed class TraceStampingConformanceFacts(TraceStampingFixture fixture) : IClassFixture<TraceStampingFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Stamp[]> Journal(INeuronRuntime runtime, NeuronId publisher, int atLeast)
    {
        var reader = runtime.Get<ITraceReader>(runtime.Id("trace.reader", "r"));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            await runtime.Idle();
            var held = await reader.Journal(publisher.ToString());
            if (held.Length >= atLeast || DateTime.UtcNow > deadline) { return held; }
            await Task.Delay(50, Ct);
        }
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task RequestSaveTakesTraceContextId(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var source = runtime.Id("trace.counter", "request");
        var trace = TraceContext.Begin("acct", runtime.Brain.Value, source);
        using (TraceContext.Enter(trace)) { await runtime.Get<ITraceCounter>(source).Add(1); }

        var saved = Assert.Single(await Journal(runtime, source, 1));
        Assert.Equal(trace.TraceId, saved.TraceId);
        Assert.Null(saved.CausationId);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task ReactionSaveInheritsTraceAndSetsCausation(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var source = runtime.Id("trace.counter", "cause");
        var relay = runtime.Id("trace.relay", "cause");
        await runtime.Get<ITraceFollower>(relay).Follow(source.ToString());
        var trace = TraceContext.Begin("acct", runtime.Brain.Value, source);
        using (TraceContext.Enter(trace)) { await runtime.Get<ITraceCounter>(source).Add(1); }

        var relayed = Assert.Single(await Journal(runtime, relay, 1));
        Assert.Equal(trace.TraceId, relayed.TraceId);
        Assert.Equal(Causation.Address(source, 1), relayed.CausationId);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task CalledNeuronInheritsTraceAndCausationFromReaction(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var source = runtime.Id("trace.counter", "call");
        var caller = runtime.Id("trace.caller", "call");
        var called = runtime.Id("trace.counter", "call-called");
        await runtime.Get<ITraceFollower>(caller).Follow(source.ToString());
        await runtime.Get<ITraceCounter>(source).Add(1);

        var cause = Assert.Single(await Journal(runtime, source, 1));
        var effect = Assert.Single(await Journal(runtime, called, 1));
        Assert.False(string.IsNullOrEmpty(cause.TraceId));
        Assert.Equal(cause.TraceId, effect.TraceId);
        Assert.Equal(Causation.Address(source, 1), effect.CausationId);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task SaveOutsideContextGetsFreshTrace(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var source = runtime.Id("trace.counter", "fresh");
        await runtime.Get<ITraceCounter>(source).Add(1);
        await runtime.Get<ITraceCounter>(source).Add(1);

        var saved = await Journal(runtime, source, 2);
        Assert.Equal(2, saved.Length);
        Assert.All(saved, item => Assert.False(string.IsNullOrEmpty(item.TraceId)));
        Assert.NotEqual(saved[0].TraceId, saved[1].TraceId);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task ReadTraceReturnsOnlyThatTrace(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var source = runtime.Id("trace.counter", "read");
        var trace = TraceContext.Begin("acct", runtime.Brain.Value, source);
        await runtime.Get<ITraceCounter>(source).Add(1);
        using (TraceContext.Enter(trace)) { await runtime.Get<ITraceCounter>(source).Add(1); }
        await runtime.Get<ITraceCounter>(source).Add(1);
        await runtime.Idle();

        var only = Assert.Single(await runtime.Get<ITraceReader>(runtime.Id("trace.reader", "r")).ReadTrace(trace.TraceId));
        Assert.Equal(trace.TraceId, only.TraceId);
        Assert.Equal(2, only.Value);
    }
}

[Module("trace-stamping-conformance")]
public sealed class TraceStampingModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [Need.State("trace-count")], [], []);
}

[Signal("trace.counted")]
public sealed record TraceCounted(int Value) : Signal;

[Signal("trace.relayed")]
public sealed record TraceRelayed(int Value) : Signal;

public sealed record TraceCount
{
    public int Value { get; init; }
}

/// <summary>A journaled signal's ids and value, as the reader hands them back.</summary>
public sealed record Stamp(string? TraceId, string? CausationId, int Value);

public interface ITraceCounter
{
    Task Add(int by);
}

public interface ITraceFollower
{
    Task Follow(string source);
}

public interface ITraceReader
{
    Task<Stamp[]> Journal(string publisher);

    Task<Stamp[]> ReadTrace(string traceId);
}

[Neuron("trace.counter", State = "trace-count")]
public sealed class TraceCounterNeuron : Neuron<TraceCount>, ITraceCounter
{
    public Task Add(int by) => Save(State with { Value = State.Value + by }, new TraceCounted(State.Value + by));
}

/// <summary>Reacts to each count by saving one of its own.</summary>
[Neuron("trace.relay", State = "trace-count")]
public sealed class TraceRelayNeuron : Neuron<TraceCount>, ITraceFollower
{
    public Task Follow(string source) => Context.Synapses.Ensure<TraceCounted>(NeuronId.Parse(source));

    [On]
    public Task On(TraceCounted signal) => Save(State with { Value = signal.Value }, new TraceRelayed(signal.Value));
}

/// <summary>Reacts to each count by calling the counter keyed with its own key plus "-called".</summary>
[Neuron("trace.caller")]
public sealed class TraceCallerNeuron : Neuron, ITraceFollower
{
    public Task Follow(string source) => Context.Synapses.Ensure<TraceCounted>(NeuronId.Parse(source));

    [On]
    public Task On(TraceCounted signal) => Get<ITraceCounter>(new NeuronId(Id.Brain, "trace.counter", Id.Key + "-called")).Add(signal.Value);
}

/// <summary>Reads the journal of the brain for the facts.</summary>
[Neuron("trace.reader"), StatelessNeuron]
public sealed class TraceReaderNeuron : Neuron, ITraceReader
{
    public async Task<Stamp[]> Journal(string publisher)
        => Of(await Context.History.Read(NeuronId.Parse(publisher), 0, 100, CancellationToken.None));

    public async Task<Stamp[]> ReadTrace(string traceId) => Of(await Context.History.ReadTrace(traceId, 0, 100, CancellationToken.None));

    private static Stamp[] Of(SignalPage page) => [.. page.Signals.Select(item => new Stamp(item.TraceId, item.CausationId,
        item.Signal switch { TraceCounted counted => counted.Value, TraceRelayed relayed => relayed.Value, _ => -1 }))];
}
