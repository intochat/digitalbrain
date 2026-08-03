using DigitalBrain.Abstractions;
using DigitalBrain.TestingTests.Harness;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class JournalReadInterleaveContracts(TestingFixture fixture)
{
    private const string ProbeName = "occupied-probe";
    private const string RecordedBeforeTheTurn = "recorded before the turn";
    private const string QueuedBehindTheTurn = "queued behind the turn";

    private static readonly TimeSpan TurnHold = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan HoldSignalBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OccupancyBudget = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(1.5);

    [Fact(DisplayName =
        "a neuron occupied by a turn answers a journal read mid-turn while every other call stays queued behind that turn")]
    public async Task JournalReadAnswersWhileTheNeuronIsOccupied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var probe = test.Neuron<IOccupiedNeuronProbe>(ProbeName);

        await probe.Reference.Announce(RecordedBeforeTheTurn);
        var announced = await probe.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal(RecordedBeforeTheTurn, announced.Synapse.Message);

        var occupied = probe.Reference.HoldTurn(TurnHold);

        // The hold emits before it sleeps and watcher pushes are one-way, so seeing this fact proves
        // the probe is inside the held turn — every call issued from here on arrives at an occupied
        // neuron.
        var holding = await probe.Outgoing.NextAsync<Greeted>(cancellationToken)
            .WaitAsync(HoldSignalBudget, cancellationToken);
        Assert.Equal(OccupiedNeuronProbe.HoldingMessage, holding.Synapse.Message);

        var queued = probe.Reference.Announce(QueuedBehindTheTurn);
        await Assert.ThrowsAsync<TimeoutException>(
            () => queued.WaitAsync(OccupancyBudget, cancellationToken));

        var session = test.Cluster.Client.GetGrain<ISessionNeuron>(
            ISessionNeuron.ForOwner(probe.Id.Owner).ToGrainId());
        var read = await session
            .ReadNeuronJournal(probe.Id, JournalKind.Outgoing, afterSequence: 0)
            .WaitAsync(ReadBudget, cancellationToken);

        Assert.False(occupied.IsCompleted);
        Assert.Contains(read.Delta, delivery => Announced(delivery, RecordedBeforeTheTurn));
        Assert.Contains(read.Delta, delivery => Announced(delivery, OccupiedNeuronProbe.HoldingMessage));
        Assert.DoesNotContain(read.Delta, delivery => Announced(delivery, QueuedBehindTheTurn));

        await occupied;
        await queued;
    }

    private static bool Announced(SynapseDelivery delivery, string message)
        => delivery.Synapse is Greeted greeted
        && string.Equals(greeted.Message, message, StringComparison.Ordinal);
}
