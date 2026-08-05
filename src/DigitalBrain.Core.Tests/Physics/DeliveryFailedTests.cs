using DigitalBrain.Testing;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class DeliveryFailedTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    [Fact(DisplayName = "Ask when the catalog has no answerer for the question journals DeliveryFailed with a no-answerer reason without burning the retry horizon")]
    public async Task AskWithNoAnswererJournalsDeliveryFailed()
    {
        var ct = Cancellation;
        var session = Brain.Session("no-answerer");
        var question = new AskExpired(new SynapseRef(new NeuronId("probe", "seed"), 1), "orphan");

        var failure = await Assert.ThrowsAsync<AskFailedException>(
            () => session.AskAsync<AskExpired>(question, ct));

        var journaled = Assert.IsType<DeliveryFailed>(failure.Fact);
        Assert.Equal("no-answerer", journaled.Reason);
        Assert.Equal(0, journaled.Attempts);

        var reading = await ReadAsync(session.Id, ct);
        var askSaid = reading.SaidSingle<AskExpired>();
        Assert.Empty(askSaid.To ?? []);

        var failedSaid = reading.SaidSingle<DeliveryFailed>();
        var body = Assert.IsType<DeliveryFailed>(failedSaid.Body);
        Assert.Equal(new SynapseRef(session.Id, askSaid.Position), body.Fact);
        Assert.Equal("no-answerer", body.Reason);
        Assert.Equal(0, body.Attempts);
        Assert.Equal(journaled, body);
    }

    [Fact(DisplayName = "Unknown kind fails terminal on attempt one with DeliveryFailed on the sender")]
    public async Task UnknownKindFailsTerminalOnAttemptOne()
    {
        var ct = Cancellation;
        var session = Brain.Session("unknown-kind");
        var missing = new NeuronId("ghost", "nobody");
        var fact = new AskExpired(new SynapseRef(new NeuronId("probe", "seed"), 2), "undeliverable");

        await session.SendAsync(missing, fact, ct);

        var reading = await WaitForJournalAsync(
            session.Id,
            observed => observed.AllSaid<DeliveryFailed>().Count == 1,
            "a DeliveryFailed for the unknown kind",
            ct);

        var sent = reading.SaidSingle<AskExpired>();
        Assert.Equal("directed", sent.DeliveryTo(missing).Via);

        var failedSaid = reading.SaidSingle<DeliveryFailed>();
        var body = Assert.IsType<DeliveryFailed>(failedSaid.Body);
        Assert.Equal(new SynapseRef(session.Id, sent.Position), body.Fact);
        Assert.Equal(missing, body.Receiver);
        Assert.Equal(1, body.Attempts);
        Assert.Contains("ghost", body.Reason, StringComparison.Ordinal);
        Assert.Contains("catalog", body.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
