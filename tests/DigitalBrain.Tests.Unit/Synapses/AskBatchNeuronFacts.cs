using DigitalBrain.Units;
using System.Text.Json;
using DigitalBrain.Neurons;
using DigitalBrain.Tests.Unit.Neurons;

namespace DigitalBrain.Tests.Unit.Synapses;

internal sealed class FakeAsks : IAsks
{
    public List<AskSettlement> Settlements { get; } = [];
    public int Woken { get; private set; }

    public Task Attach(CancellationToken ct) => Task.CompletedTask;

    public Task Detach(CancellationToken ct) => Task.CompletedTask;

    public Task Settled(AskSettlement settlement)
    {
        Settlements.Add(settlement);
        return Task.CompletedTask;
    }

    public Task Wake()
    {
        Woken++;
        return Task.CompletedTask;
    }
}

public sealed class AskBatchNeuronFacts
{
    private static readonly NeuronId Id = new(new BrainId("alice"), "unit.ask.batch", "asker~b1");
    private static readonly NeuronId AsksId = new(Id.Brain, "unit.asks", "asker");

    public AskBatchNeuronFacts()
    {
        Bound = new() { ["feed"] = Probe };
        Resolver = new FakeSlotResolver(Bound);
        Context.Neurons[AsksId] = Asks;
    }

    private RecordingContext Context { get; } = new(Id);
    private FakeAsks Asks { get; } = new();
    private FakeProbe Probe { get; } = new();
    private Dictionary<string, object> Bound { get; }
    private FakeSlotResolver Resolver { get; }
    private SynapseCatalogSource Catalog { get; } = new(SynapseCatalog.Scan([typeof(Asker), typeof(AskerFunctions)]));

    private static Ask Ask(string key = "k") => new("feed", "Get", JsonElement.Parse("""{"id":"1"}"""), key);

    private AskBatchNeuron Attach(RecordingContext? context = null)
    {
        var neuron = new AskBatchNeuron(Catalog, Resolver);
        NeuronAttachment.Attach(neuron, context ?? Context);
        return neuron;
    }

    private async Task<AskSettlement> One(Func<Ask, Ask>? change = null)
    {
        var context = new RecordingContext(Id) { Neurons = { [AsksId] = Asks } };
        var batch = Attach(context);
        var ask = Ask();
        await batch.Put("asker", [change is null ? ask : change(ask)]);
        var before = Asks.Settlements.Count;
        await batch.Run(0, 1);
        Assert.Equal(before + 1, Asks.Settlements.Count);
        return Asks.Settlements[^1];
    }

    [Fact]
    public async Task PutStoresOnceAndAllReturnsTheAsks()
    {
        var batch = Attach();
        await batch.Put("asker", [Ask("k1"), Ask("k2")]);
        await batch.Put("asker", [Ask("other")]);

        Assert.Single(Context.Writes);
        Assert.Equal(["k1", "k2"], (await batch.All()).Select(a => a.Key));
    }

    [Fact]
    public async Task RunCallsTheCapabilityThroughTheResolverAndReportsOk()
    {
        var s = await One();
        Assert.Equal(("b1", 0, 1, "feed", "Get", "k", false), (s.Batch, s.Index, s.Attempt, s.Slot, s.Operation, s.Key, s.Repeatable));
        Assert.Equal((AnswerStatus.Ok, """{"id":"1"}""", null, false), (s.Status, s.Result.GetRawText(), s.Error, s.Transient));
        Assert.Equal(new NeuronId(Id.Brain, "asker", "default"), Resolver.LastState);
    }

    [Fact]
    public async Task AnUnboundSlotIsFailedUnboundAndNotTransient()
    {
        Bound.Clear();
        var s = await One();
        Assert.Equal((AnswerStatus.Failed, "unbound", false), (s.Status, s.Error, s.Transient));
        Assert.Equal(JsonValueKind.Null, s.Result.ValueKind);
    }

    [Fact]
    public async Task ASlotTheLiveCatalogDoesNotDeclareIsRefused()
    {
        var s = await One(a => a with { Slot = "gone" });
        Assert.Equal((AnswerStatus.Refused, "slot-dropped", false), (s.Status, s.Error, s.Transient));
    }

    [Fact]
    public async Task ACapabilityExceptionIsFailedAndTransient()
    {
        Probe.OnGet = (_, _) => throw new InvalidOperationException("down");
        var s = await One();
        Assert.Equal((AnswerStatus.Failed, "down", true), (s.Status, s.Error, s.Transient));
    }

    [Fact]
    public async Task ABadOperationIsFailedAndNotTransient()
    {
        var s = await One(a => a with { Operation = "Nope" });
        Assert.Equal((AnswerStatus.Failed, false), (s.Status, s.Transient));
        Assert.StartsWith("bad-arguments", s.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResultOverTheCapIsFailedTooLarge()
    {
        Probe.OnGet = (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(new string('a', AskLimits.MaxResultBytes + 1)));
        var s = await One();
        Assert.Equal((AnswerStatus.Failed, "too-large", false), (s.Status, s.Error, s.Transient));
    }

    [Fact]
    public async Task ATimeoutIsUnknownForACommandAndFailedForARepeatableAsk()
    {
        Probe.OnGet = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return default;
        };
        var command = await One(a => a with { Timeout = TimeSpan.FromMilliseconds(50) });
        var repeatable = await One(a => a with { Timeout = TimeSpan.FromMilliseconds(50), Repeatable = true });

        Assert.Equal((AnswerStatus.Unknown, "timeout", true), (command.Status, command.Error, command.Transient));
        Assert.Equal((AnswerStatus.Failed, "timeout", true), (repeatable.Status, repeatable.Error, repeatable.Transient));
    }

    [Fact]
    public async Task RunOnADroppedStoreReportsNothing()
    {
        var batch = Attach();
        await batch.Put("asker", [Ask("k")]);
        await batch.Drop();
        await batch.Run(0, 1);

        Assert.Empty(Asks.Settlements);
    }

    [Fact]
    public async Task ACapabilityOwnCancellationAndLongMessageSettleFailedTransientCappedAtFiveHundred()
    {
        Probe.OnGet = (_, _) => Task.FromException<JsonElement>(new OperationCanceledException(new string('x', 600)));
        var s = await One();
        Assert.Equal((AnswerStatus.Failed, 500, true), (s.Status, s.Error?.Length, s.Transient));

        var reloaded = Attach();
        Context.Store(new AskBatchState("asker", [Ask("reloaded")]));
        await NeuronAttachment.Activate(reloaded, TestContext.Current.CancellationToken);
        await reloaded.Run(5, 1); // out-of-range index reports nothing
        Assert.Single(Asks.Settlements);
        Assert.Equal(["reloaded"], (await reloaded.All()).Select(a => a.Key));
    }
}
