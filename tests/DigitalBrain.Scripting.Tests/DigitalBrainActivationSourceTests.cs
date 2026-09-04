using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Scripting.Startup;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class DigitalBrainActivationSourceTests
{
    [Fact]
    public async Task Existing_activation_is_emitted_before_live_watch()
    {
        var delivery = Activation("alice", sequence: 3);
        var brain = new FakeDigitalBrain("alice")
        {
            InitialJournal = new JournalRead(7,
            [
                Delivery(new UnrelatedSignal(), "alice", sequence: 1),
                Activation("bob", sequence: 2),
                delivery,
            ], null),
            WatchJournal = static (_, _, _) => throw new InvalidOperationException("Live watch started before history was emitted."),
        };
        var source = new DigitalBrainActivationSource(brain);

        await using var enumerator = source.WatchAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("alice", enumerator.Current.Owner);
        Assert.Equal(delivery.SignalId.Value.ToString("D"), enumerator.Current.SignalId);
        Assert.False(brain.WasWatchJournalCalled);
        Assert.Equal(JournalKind.Outgoing, brain.ReadJournalKind);
        Assert.Equal(0, brain.ReadJournalAfterSequence);
    }

    [Fact]
    public async Task Watch_starts_at_the_history_resume_sequence()
    {
        var delivery = Activation("alice", sequence: 10);
        var brain = new FakeDigitalBrain("alice")
        {
            InitialJournal = new JournalRead(7, [], null),
            WatchJournal = (_, _, _) => JournalReads(new JournalRead(8,
            [
                Delivery(new UnrelatedSignal(), "alice", sequence: 8),
                Activation("bob", sequence: 9),
                delivery,
            ], null)),
        };
        var source = new DigitalBrainActivationSource(brain);

        await using var enumerator = source.WatchAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(7, brain.WatchJournalAfterSequence);
        Assert.Equal("alice", enumerator.Current.Owner);
        Assert.Equal(delivery.SignalId.Value.ToString("D"), enumerator.Current.SignalId);
    }

    [Fact]
    public async Task Supplied_cancellation_is_forwarded_to_journal_operations()
    {
        using var cancellation = new CancellationTokenSource();
        var delivery = Activation("alice", sequence: 8);
        var brain = new FakeDigitalBrain("alice")
        {
            InitialJournal = new JournalRead(7, [], null),
            WatchJournal = (_, _, _) => JournalReads(new JournalRead(8, [delivery], null)),
        };
        var source = new DigitalBrainActivationSource(brain);

        await using var enumerator = source.WatchAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(cancellation.Token, brain.ReadJournalCancellationToken);
        Assert.Equal(cancellation.Token, brain.WatchJournalCancellationToken);
    }

    private static SignalDelivery Activation(string owner, long sequence) => SignalDelivery.Create(
        new DigitalBrainActivated(new OwnerId(owner)),
        new NeuronId("root", new OwnerId(owner), "default"),
        sequence,
        TimeProvider.System);

    private static SignalDelivery Delivery(Signal signal, string owner, long sequence) => SignalDelivery.Create(
        signal,
        new NeuronId("root", new OwnerId(owner), "default"),
        sequence,
        TimeProvider.System);

    private static async IAsyncEnumerable<JournalRead> JournalReads(params JournalRead[] reads)
    {
        foreach (var read in reads)
        {
            yield return read;
        }
    }

    private sealed record UnrelatedSignal : Signal;
}
