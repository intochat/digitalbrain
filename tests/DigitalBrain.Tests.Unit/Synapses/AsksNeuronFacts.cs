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
    private AsksNeuron? _asks;

    private static Ask Ask(string key, string slot = "feed") => new(slot, "Get", JsonElement.Parse("""{"id":"1"}"""), key);

    private static Asked Batch(string id, int count) =>
        new("asker", id, [.. Enumerable.Range(0, count).Select(i => Ask($"k{i}"))]);

    private static AskSettlement Settle(
        string batch,
        int index,
        int attempt,
        AnswerStatus status = AnswerStatus.Ok,
        bool transient = false,
        bool repeatable = false,
        string? error = null,
        JsonElement? result = null) =>
        new(
            batch,
            index,
            attempt,
            "feed",
            "Get",
            $"k{index}",
            repeatable,
            status,
            result ?? (status == AnswerStatus.Ok ? JsonElement.Parse("""{"id":"1"}""") : AskRules.NoResult),
            error,
            transient);

    private void Seed(AsksState state) => Context.Store(state);

    private Task Fire(string name) => _asks!.Remind(name, Ct);

    private List<Answer> Sent() =>
        [.. Context.Writes.SelectMany(w => w.Signals.OfType<Answered>()).SelectMany(a => a.Answers)];

    private async Task<AsksNeuron> Activated()
    {
        var asks = new AsksNeuron(Catalog);
        NeuronAttachment.Attach(asks, Context);
        await NeuronAttachment.Activate(asks, Ct);
        _asks = asks;
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

    [Fact]
    public async Task ASettlementForAnAttemptNotInFlightIsIgnored()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 1), Ct);
        var writes = Context.Writes.Count;
        await asks.Settled(Settle("b1", 0, attempt: 2));
        await asks.Settled(Settle("b9", 0, attempt: 1));
        await asks.Settled(Settle("b1", 5, attempt: 1));
        Assert.Equal(writes, Context.Writes.Count);

        await asks.Settled(Settle("b1", 0, attempt: 1));
        await asks.Settled(Settle("b1", 0, attempt: 1));                          // a second copy
        var answer = Assert.Single(Sent());
        Assert.Equal(("feed", "Get", "k0", AnswerStatus.Ok, 0), (answer.Slot, answer.Operation, answer.Key, answer.Status, answer.Remaining));
        Assert.True(B1.Dropped);
        Assert.Contains("asks-nudge", Timers.StoppedReminders);
    }

    [Fact]
    public async Task AnswersLeaveInGroupsOfTwentyFive()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 30), Ct);
        for (var i = 0; i < 30; i++) { await asks.Settled(Settle("b1", i, 1)); }

        Assert.Equal([25, 5], Context.Writes.SelectMany(w => w.Signals.OfType<Answered>()).Select(a => a.Answers.Count));
        Assert.Equal(Enumerable.Range(0, 30).Select(i => $"k{i}"), Sent().Select(a => a.Key));
        Assert.Equal(0, Sent()[^1].Remaining);
    }

    [Fact]
    public async Task AGroupClosesBeforeExceedingTheResultByteCap()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 3), Ct);
        var half = JsonElement.Parse("\"" + new string('a', (AskLimits.MaxResultBytes / 2) - 2) + "\"");
        var extra = JsonElement.Parse("""{"id":"2"}""");
        await asks.Settled(Settle("b1", 0, 1, result: half));
        await asks.Settled(Settle("b1", 1, 1, result: half));
        await asks.Settled(Settle("b1", 2, 1, result: extra));

        Assert.Equal([2, 1], Context.Writes.SelectMany(w => w.Signals.OfType<Answered>()).Select(a => a.Answers.Count));
    }

    [Fact]
    public async Task AfterOneSecondWaitingAnswersAreSent()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 6), Ct);
        await asks.Settled(Settle("b1", 0, 1));
        Assert.Empty(Sent());                                                     // four are still flying
        Clock.Now += AskLimits.AnswerFlush;
        await Fire("asks-due");
        Assert.Equal("k0", Assert.Single(Sent()).Key);
    }

    [Fact]
    public async Task ATransientFailureOfARepeatableAskIsRetriedAfterTwoThenTenSecondsThenFailed()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 1), Ct);
        await asks.Settled(Settle("b1", 0, 1, AnswerStatus.Failed, transient: true, repeatable: true, error: "down"));
        Assert.Equal([(0, 1)], B1.Runs);
        Clock.Now += TimeSpan.FromSeconds(2);
        await Fire("asks-due");
        Assert.Equal([(0, 1), (0, 2)], B1.Runs);
        await asks.Settled(Settle("b1", 0, 2, AnswerStatus.Failed, transient: true, repeatable: true, error: "down"));
        Clock.Now += TimeSpan.FromSeconds(10);
        await Fire("asks-due");
        Assert.Equal([(0, 1), (0, 2), (0, 3)], B1.Runs);
        Assert.Empty(Sent());
        await asks.Settled(Settle("b1", 0, 3, AnswerStatus.Failed, transient: true, repeatable: true, error: "down"));

        Assert.Equal((AnswerStatus.Failed, "down"), (Assert.Single(Sent()).Status, Sent()[0].Error));
        Assert.Equal(AskLimits.MaxAttempts, B1.Runs.Count);
    }

    [Fact]
    public async Task ACommandOrAFailureThatIsNotTransientIsAnsweredAtOnce()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 2), Ct);
        await asks.Settled(Settle("b1", 0, 1, AnswerStatus.Failed, transient: true, repeatable: false, error: "down"));
        await asks.Settled(Settle("b1", 1, 1, AnswerStatus.Failed, transient: false, repeatable: true, error: "unbound"));

        Assert.Equal(["down", "unbound"], Sent().Select(a => a.Error));
        Assert.Equal(2, B1.Runs.Count);
    }

    [Fact]
    public async Task AFlyingAskPastItsDueIsAnsweredUnknownOrRetried()
    {
        var asks = await Activated();
        await asks.OnAsked(new Asked("asker", "b1", [Ask("cmd"), Ask("read") with { Repeatable = true }]), Ct);
        Clock.Now += AskLimits.DefaultTimeout + AsksNeuron.Grace;
        await Fire("asks-nudge");                                                 // cmd is answered Unknown (waits to be sent: read is waiting), read waits two seconds
        Assert.DoesNotContain((1, 2), B1.Runs);
        Clock.Now += TimeSpan.FromSeconds(2);
        await Fire("asks-due");

        Assert.Contains((1, 2), B1.Runs);
        var unknown = Assert.Single(Sent());
        Assert.Equal(("cmd", AnswerStatus.Unknown, "timeout", JsonValueKind.Null), (unknown.Key, unknown.Status, unknown.Error, unknown.Result.ValueKind));
        await asks.Settled(Settle("b1", 0, 1));                                   // the lost attempt reports after all: ignored
        Assert.Single(Sent());
    }

    [Fact]
    public async Task ABatchOlderThanTwentyFourHoursExpiresEveryOpenAsk()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 8), Ct);
        Clock.Now += AskLimits.MaxAge;
        await Fire("asks-nudge");

        Assert.Equal(8, Sent().Count);
        Assert.All(Sent(), a => Assert.Equal((AnswerStatus.Expired, "expired"), (a.Status, a.Error)));
        Assert.True(B1.Dropped);
    }

    [Fact]
    public async Task AFreshActivationResumesOpenBatches()
    {
        Seed(new AsksState([new OpenBatch("b1", 2, 0, [], [], 0, AskLimits.DefaultTimeout, Clock.Now)], [], null, Clock.Now.ToUnixTimeSeconds() / 3600, 2, false));
        B1.Stored = [Ask("k0"), Ask("k1")];
        _ = await Activated();

        Assert.Equal([(0, 1), (1, 1)], B1.Runs);
        Assert.Contains(("asks-nudge", TimeSpan.FromMinutes(1)), Timers.Reminders);
    }

    [Fact]
    public async Task DetachDropsEverythingWithoutAnswers()
    {
        var asks = await Activated();
        await asks.OnAsked(Batch("b1", 3), Ct);
        await asks.Detach(Ct);
        await asks.Settled(Settle("b1", 0, 1));

        Assert.Empty(Sent());
        Assert.True(B1.Dropped);
        Assert.Contains(new NeuronId(Id.Brain, "asker", "default"), Context.Removed);
        Assert.Contains("asks-nudge", Timers.StoppedReminders);
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
