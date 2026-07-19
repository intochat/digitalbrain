using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class JournalCompactionContracts
{
    private const int DeliveriesPastTheBound = 1200;

    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    [Fact(DisplayName = "a journal past its bound keeps its tally and evicts only the delta log")]
    public async Task AJournalPastItsBoundKeepsItsTally()
    {
        var recorded = await DeliverPastTheBoundAsync("compaction-tally");

        Assert.Equal(DeliveriesPastTheBound, recorded.TotalRecorded);
        Assert.Equal(DeliveriesPastTheBound, recorded.RecordedOf(typeof(Ping).FullName!));
        Assert.True(
            recorded.RetainedCount < DeliveriesPastTheBound,
            $"The delta log retained all {recorded.RetainedCount} deliveries, so nothing was evicted and the bound is not being applied.");
    }

    [Fact(DisplayName = "compaction leaves the sequence window consistent with the last sequence")]
    public async Task CompactionLeavesTheSequenceWindowConsistent()
    {
        var recorded = await DeliverPastTheBoundAsync("compaction-sequence");

        Assert.Equal(DeliveriesPastTheBound, recorded.LastSequence);
        Assert.Equal(recorded.LastSequence, recorded.EarliestRetainedSequence + recorded.RetainedCount - 1);
    }

    private static async Task<JournalSnapshot> DeliverPastTheBoundAsync(string owner)
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain(owner);

        for (var delivery = 0; delivery < DeliveriesPastTheBound; delivery++)
        {
            await simulation.SendAsync("Ping", nameof(Echo), "target", NoValues);
        }

        return await simulation.ReadJournalSnapshotAsync(JournalKind.Incoming, nameof(Echo), "target");
    }
}
