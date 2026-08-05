using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests;

public sealed class ZeroReceiverEmitTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    [Fact(DisplayName = "Emit with no declared listeners and no connections journals a said entry with empty receivers and does not throw")]
    public async Task ZeroReceiverEmitIsLegal()
    {
        var ct = Cancellation;
        var session = Brain.Session("zero-receiver");
        var announcement = new AskExpired(new SynapseRef(new NeuronId("audit", "seed"), 7), "announce-only");

        await session.EmitAsync(announcement, ct);

        var reading = await WaitForJournalAsync(
            session.Id,
            observed => observed.AllSaid<AskExpired>().Count == 1,
            "a said AskExpired with empty receivers",
            ct);

        var said = reading.SaidSingle<AskExpired>();
        Assert.NotNull(said.To);
        Assert.Empty(said.To);
        Assert.Null(said.Cause);
        Assert.Equal(announcement, Assert.IsType<AskExpired>(said.Body));
        Assert.Empty(reading.AllSaid<DeliveryFailed>());
    }
}
