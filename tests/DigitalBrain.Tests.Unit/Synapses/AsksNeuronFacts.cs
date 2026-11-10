using System.Text.Json;
using DigitalBrain.Spi;
using DigitalBrain.Tests.Unit.Neurons;

namespace DigitalBrain.Tests.Unit.Synapses;

public sealed class AsksNeuronFacts
{
    private static readonly NeuronId Id = new(new BrainId("alice"), "unit.asks", "asker");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AsksNeuronFacts()
    {
        Context.Time = Clock;
        Context.RecordedTimers = Timers;
        Context.RecordedReminders = Timers;
        Context.Neurons[AskRules.BatchOf(Id, "b1")] = B1;
        Context.Neurons[AskRules.BatchOf(Id, "b2")] = B2;
        Context.Neurons[AskRules.BatchOf(Id, "b3")] = B3;
    }

    private RecordingContext Context { get; } = new(Id);
    private TestClock Clock { get; } = new();
    private FakeTimers Timers { get; } = new();
    private FakeBatch B1 { get; } = new();
    private FakeBatch B2 { get; } = new();
    private FakeBatch B3 { get; } = new();
    private SynapseCatalogSource Catalog { get; } = new(SynapseCatalog.Scan([typeof(Asker), typeof(AskerFunctions)]));

    private static Ask Ask(string key, string slot = "feed") => new(slot, "Get", JsonElement.Parse("""{"id":"1"}"""), key);

    private static Asked Batch(string id, int count) =>
        new("asker", id, [.. Enumerable.Range(0, count).Select(i => Ask($"k{i}"))]);

    private void Seed(AsksState state) => Context.Store(state);

    private List<Answer> Sent() =>
        [.. Context.Writes.SelectMany(w => w.Signals.OfType<Answered>()).SelectMany(a => a.Answers)];

    private async Task<AsksNeuron> Activated()
    {
        var asks = new AsksNeuron(Catalog);
        NeuronAttachment.Attach(asks, Context);
        await NeuronAttachment.Activate(asks, Ct);
        return asks;
    }

    [Fact]
    public async Task IntakeStoresTheBatchOnceAndStartsFour()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 10), Ct);
        await asks.OnAsked(Batch("b1", 10), Ct);                                  // delivered twice

        Assert.Equal(10, Assert.Single(B1.Puts).Count);
        Assert.Equal([(0, 1), (1, 1), (2, 1), (3, 1)], B1.Runs);
        Assert.Contains(("asks-nudge", TimeSpan.FromMinutes(1)), Timers.Reminders);
        Assert.Empty(Sent());
    }

    [Fact]
    public async Task ABatchForAnotherUnitOrOneThatBreaksTheRulesIsDropped()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 1) with { Unit = "other" }, Ct);
        await asks.OnAsked(new Asked("asker", "b1", [Ask("k", slot: "undeclared")]), Ct);
        await asks.OnAsked(Batch("b1", AskLimits.MaxAsksPerReaction + 1), Ct);

        Assert.Empty(B1.Puts);
        Assert.Empty(Context.Writes);
    }

    [Fact]
    public async Task OverTheOpenLimitEveryAskIsRefusedOnceThenBatchesAreDropped()
    {
        Seed(new AsksState([new OpenBatch("b0", AskLimits.MaxOpenAsks, AskLimits.MaxOpenAsks, [], [], 0, AskLimits.DefaultTimeout, Clock.Now)], [], null, Clock.Now.ToUnixTimeSeconds() / 3600, 0, false));
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 2), Ct);
        await asks.OnAsked(Batch("b2", 2), Ct);

        Assert.Equal([(AnswerStatus.Refused, "over-budget", AskLimits.MaxOpenAsks), (AnswerStatus.Refused, "over-budget", AskLimits.MaxOpenAsks)],
            Sent().Select(a => (a.Status, a.Error, a.Remaining)));
        Assert.Empty(B1.Puts);
        Assert.Empty(B2.Puts);
    }

    [Fact]
    public async Task TheHourlyLimitTripsAndTheTripClearsWhenTheHourTurns()
    {
        Seed(new AsksState([], [], null, Clock.Now.ToUnixTimeSeconds() / 3600, AskLimits.MaxAsksPerHour, false));
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 1), Ct);                                   // refused, trips
        await asks.OnAsked(Batch("b2", 1), Ct);                                   // dropped
        Clock.Now += TimeSpan.FromHours(1);
        await asks.OnAsked(Batch("b3", 1), Ct);                                   // accepted

        Assert.Equal("over-budget", Assert.Single(Sent()).Error);
        Assert.Empty(B2.Puts);
        Assert.Single(B3.Puts);
    }

    [Fact]
    public async Task ADuplicateOpenKeyIsRefusedAlreadyOpenAndTheRestRun()
    {
        var asks = await Activated();
        await asks.OnAsked(new Asked("asker", "b1", [Ask("feed-key")]), Ct);       // flying, not answered
        await asks.OnAsked(new Asked("asker", "b2", [Ask("feed-key"), Ask("other")]), Ct);

        var refused = Assert.Single(Sent());
        Assert.Equal(("feed-key", AnswerStatus.Refused, "already-open", 2), (refused.Key, refused.Status, refused.Error, refused.Remaining));
        Assert.Equal(["other"], Assert.Single(B2.Puts).Select(a => a.Key));
        Assert.Equal([(0, 1)], B1.Runs);
        Assert.Equal([(0, 1)], B2.Runs);
    }

    [Fact]
    public async Task AttachHoldsOneSynapseOnTheUnitsStateNeuron()
    {
        var asks = await Activated();
        await asks.Attach(Ct);
        await asks.Attach(Ct);
        var synapse = Assert.Single(Context.SynapseList);
        Assert.Equal((new NeuronId(Id.Brain, "asker", "default"), typeof(Asked)), (synapse.From, synapse.Signal));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeBatch : IAskBatch
    {
        public List<IReadOnlyList<Ask>> Puts { get; } = [];
        public List<(int Index, int Attempt)> Runs { get; } = [];
        public IReadOnlyList<Ask> Stored { get; set; } = [];
        public bool Dropped { get; private set; }

        public Task Put(string unit, IReadOnlyList<Ask> asks)
        {
            IReadOnlyList<Ask> copy = [.. asks];
            Puts.Add(copy);
            if (Stored.Count == 0)
            {
                Stored = copy;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Ask>> All() => Task.FromResult(Stored);

        public Task Run(int index, int attempt)
        {
            Runs.Add((index, attempt));
            return Task.CompletedTask;
        }

        public Task Drop()
        {
            Dropped = true;
            Stored = [];
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimers : ITimers, IReminders
    {
        public List<(string Name, TimeSpan Due, TimeSpan? Period, bool KeepAlive)> StartedTimers { get; } = [];
        public List<string> StoppedTimers { get; } = [];
        public List<(string Name, TimeSpan Period)> Reminders { get; } = [];
        public List<string> StoppedReminders { get; } = [];

        public void Start(string name, TimeSpan due, TimeSpan? period) =>
            StartedTimers.Add((name, due, period, false));

        public void Start(string name, TimeSpan due, TimeSpan? period, bool keepAlive) =>
            StartedTimers.Add((name, due, period, keepAlive));

        public void Stop(string name) => StoppedTimers.Add(name);

        Task IReminders.Start(string name, TimeSpan due, TimeSpan period)
        {
            Reminders.Add((name, period));
            return Task.CompletedTask;
        }

        Task IReminders.Stop(string name)
        {
            StoppedReminders.Add(name);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> Names() =>
            Task.FromResult<IReadOnlyList<string>>([.. Reminders.Select(r => r.Name).Except(StoppedReminders, StringComparer.Ordinal).Distinct(StringComparer.Ordinal)]);
    }
}
