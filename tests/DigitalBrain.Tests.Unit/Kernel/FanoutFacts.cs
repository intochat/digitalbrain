using DigitalBrain.Kernel;

namespace DigitalBrain.Tests.Unit.Kernel;

public sealed class FanoutFacts
{
    private sealed record TestSignal(string Data) : Signal;

    private sealed class FakeNudge : INudge
    {
        public List<(NeuronId Sub, Position UpTo)> Nudges { get; } = [];
        public void Nudge(NeuronId subscriber, Position upTo) => Nudges.Add((subscriber, upTo));
    }

    private sealed class FakeView : IJournalView
    {
        public Position Last { get; set; } = new(10);
        public SubscriptionIndex Subscriptions { get; } = new();
        public Position FloorOf(NeuronId? publisher) => new(0);
        public Position LastOf(NeuronId publisher) => Last;
        public IEnumerable<AliasMark> Marks(NeuronId? publisher) => [];
        public long SequenceAt(NeuronId publisher, Position position) => position.Value;
        public Task<JournalPage> ReadAfter(Position after, NeuronId? publisher, int limit, CancellationToken ct)
            => Task.FromResult(new JournalPage(new Position(0), Last, []));
    }

    [Fact]
    public async Task Fanout_nudges_interested_subscribers()
    {
        var view = new FakeView();
        var nudge = new FakeNudge();
        var pub = NeuronId.Parse("b1/pub/1");
        var sub1 = NeuronId.Parse("b1/sub/1");
        var sub2 = NeuronId.Parse("b1/sub/2");

        var signal = new TestSignal("hello");
        var alias = SignalAliases.Of(typeof(TestSignal));

        view.Subscriptions.Fold(sub1, Subscriptions.None(sub1).Add(pub, [alias], new Position(0), DateTimeOffset.UtcNow).Encode(), StateChange.Write);
        view.Subscriptions.Fold(sub2, Subscriptions.None(sub2).Add(pub, ["other.alias"], new Position(0), DateTimeOffset.UtcNow).Encode(), StateChange.Write);

        var list = new List<Signal>();
        var fanout = new Fanout(view, nudge, a => a == alias ? typeof(TestSignal) : null, (_, sigs) => list.AddRange(sigs));

        var jSignal = new JournaledSignal(pub, 1, signal, DateTimeOffset.UtcNow);
        await fanout.Appended(pub, [jSignal], new Position(10), CancellationToken.None);

        // Only sub1 should be nudged
        Assert.Single(nudge.Nudges);
        Assert.Equal(sub1, nudge.Nudges[0].Sub);
        Assert.Equal(new Position(10), nudge.Nudges[0].UpTo);
        Assert.Single(list);
    }
}
