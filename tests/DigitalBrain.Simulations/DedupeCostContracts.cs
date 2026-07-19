using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class DedupeCostContracts
{
    private const int BatchSize = 1000;

    private const double ToleratedGrowth = 2.0;

    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    [Fact(DisplayName = "dedupe cost per delivery does not grow with journal length")]
    public async Task DedupeCostPerDeliveryDoesNotGrowWithJournalLength()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("dedupe-cost");

        var intoEmptyJournal = await AllocatedOverBatchAsync(simulation);
        var intoFullJournal = await AllocatedOverBatchAsync(simulation);

        var delivered = await simulation.ReadJournalSnapshotAsync(JournalKind.Incoming, nameof(Echo), "cost-target");

        Assert.Equal(BatchSize * 2, delivered.TotalRecorded);

        Assert.True(
            intoFullJournal < intoEmptyJournal * ToleratedGrowth,
            $"Delivering {BatchSize} synapses into a journal already holding {BatchSize} allocated {intoFullJournal / 1_000_000d:0.0} MB, "
            + $"against {intoEmptyJournal / 1_000_000d:0.0} MB into an empty one. Every delivery deserializes the whole incoming journal to dedupe.");
    }

    private static async Task<long> AllocatedOverBatchAsync(Simulation simulation)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);

        for (var delivery = 0; delivery < BatchSize; delivery++)
        {
            await simulation.SendAsync("Ping", nameof(Echo), "cost-target", NoValues);
        }

        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }
}
