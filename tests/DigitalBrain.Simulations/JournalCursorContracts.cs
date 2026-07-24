using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class JournalCursorContracts
{
    private const int DeliveriesPastTheBound = 1200;

    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    [Fact(DisplayName = "a journal cursor returns only synapses recorded after it")]
    public async Task AJournalCursorReturnsOnlySynapsesRecordedAfterIt()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("cursor-delta");

        await simulation.SendAsync("Ping", nameof(Echo), "target", NoValues);

        var first = await simulation.ReadJournalAsync(JournalKind.Incoming, nameof(Echo), "target", afterSequence: 0);

        await simulation.SendAsync("Ping", nameof(Echo), "target", NoValues);

        var second = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(Echo),
            "target",
            afterSequence: first.ResumeSequence);

        Assert.Null(first.ResetSnapshot);
        Assert.Single(first.Delta);
        Assert.Null(second.ResetSnapshot);
        Assert.Single(second.Delta);
        Assert.Equal(first.ResumeSequence + 1, second.ResumeSequence);
        Assert.NotEqual(
            first.Delta.Single().SynapseId,
            second.Delta.Single().SynapseId);
    }

    [Fact(DisplayName = "a stale journal cursor receives a full snapshot and a resume sequence")]
    public async Task AStaleJournalCursorReceivesAFullSnapshotAndAResumeSequence()
    {
        var simulation = await DeliverPastTheBoundAsync("cursor-reset");

        var read = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(Echo),
            "target",
            afterSequence: 0);

        Assert.Empty(read.Delta);
        Assert.NotNull(read.ResetSnapshot);
        Assert.Equal(DeliveriesPastTheBound, read.ResetSnapshot.TotalRecorded);
        Assert.Equal(DeliveriesPastTheBound, read.ResumeSequence);
    }

    [Fact(DisplayName = "a retained journal cursor still returns its exact fact after compaction")]
    public async Task ARetainedJournalCursorStillReturnsItsExactFactAfterCompaction()
    {
        var simulation = await DeliverPastTheBoundAsync("cursor-retained", nameof(JournalRecorder));
        var reset = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(JournalRecorder),
            "target",
            afterSequence: 0);

        await simulation.SendAsync("Pong", nameof(JournalRecorder), "target", NoValues);

        var later = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(JournalRecorder),
            "target",
            afterSequence: reset.ResumeSequence);

        Assert.NotNull(reset.ResetSnapshot);
        Assert.Null(later.ResetSnapshot);
        Assert.IsType<Pong>(Assert.Single(later.Delta).Synapse);
        Assert.Equal(reset.ResumeSequence + 1, later.ResumeSequence);
    }

    [Fact(DisplayName = "a journal cursor ahead of the feed resets to the actual sequence")]
    public async Task AJournalCursorAheadOfTheFeedResetsToTheActualSequence()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("cursor-ahead");

        var read = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(Echo),
            "target",
            afterSequence: 1);

        Assert.Empty(read.Delta);
        Assert.NotNull(read.ResetSnapshot);
        Assert.Equal(0, read.ResetSnapshot.TotalRecorded);
        Assert.Equal(0, read.ResumeSequence);
    }

    [Fact(DisplayName = "a negative journal cursor fails loudly")]
    public async Task ANegativeJournalCursorFailsLoudly()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("cursor-negative");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(Echo),
            "target",
            afterSequence: -1));
    }

    [Fact(DisplayName = "settling a compacted journal waits for its sequence to stop advancing")]
    public async Task SettlingACompactedJournalWaitsForItsSequenceToStopAdvancing()
    {
        var simulation = await DeliverPastTheBoundAsync("cursor-consumer-settle");
        var settling = simulation.SettleAsync(JournalKind.Incoming, nameof(Echo), "target");

        var producer = ProduceAsync(simulation, TestContext.Current.CancellationToken);

        var completedFirst = await Task.WhenAny(settling, producer);

        Assert.Same(producer, completedFirst);

        await producer;

        var retained = await settling;

        Assert.True(retained > 0);
    }

    private static async Task<Simulation> DeliverPastTheBoundAsync(
        string owner,
        string neuronType = nameof(Echo))
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain(owner);

        for (var delivery = 0; delivery < DeliveriesPastTheBound; delivery++)
        {
            await simulation.SendAsync("Ping", neuronType, "target", NoValues);
        }

        return simulation;
    }

    private static async Task ProduceAsync(
        Simulation simulation,
        CancellationToken cancellationToken)
    {
        for (var delivery = 0; delivery < 100; delivery++)
        {
            await simulation.SendAsync("Ping", nameof(Echo), "target", NoValues);
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}

internal sealed class JournalRecorder : Neuron, IHandle<Ping>, IHandle<Pong>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task HandleAsync(Pong synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
