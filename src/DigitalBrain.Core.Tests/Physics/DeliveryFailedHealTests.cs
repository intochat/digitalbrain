using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class DeliveryFailedHealTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<FailureHealer>().AddModule<FailureHealObserver>();

    [Fact(DisplayName =
        "A heal neuron that declares INeuron of DeliveryFailed receives the Core-journaled failure and can Emit an alternate path")]
    public async Task HealNeuronHearsDeliveryFailedAndEmitsAlternate()
    {
        var ct = Cancellation;
        var context = "heal-spine";
        var session = Brain.Session(context);
        var healId = new NeuronId("failurehealer", context);
        var observerId = new NeuronId("failurehealobserver", context);
        var missing = new NeuronId("ghost", "nowhere");
        var payload = new AskExpired(new SynapseRef(new NeuronId("probe", "seed"), 9), "undeliverable-for-heal");

        await session.SendAsync(missing, payload, ct);

        var sessionReading = await WaitForJournalAsync(
            session.Id,
            observed => observed.AllSaid<DeliveryFailed>().Count == 1,
            "a said DeliveryFailed on the directed sender",
            ct);

        var sent = sessionReading.SaidSingle<AskExpired>();
        Assert.Equal("ask", sent.DeliveryTo(missing).Via);
        Assert.Single(sent.To ?? []);

        var failedSaid = sessionReading.SaidSingle<DeliveryFailed>();
        var failure = Assert.IsType<DeliveryFailed>(failedSaid.Body);
        Assert.Equal(new SynapseRef(session.Id, sent.Position), failure.Fact);
        Assert.Equal(missing, failure.Receiver);
        Assert.Equal(1, failure.Attempts);
        Assert.Equal("declared", failedSaid.DeliveryTo(healId).Via);

        var healReading = await WaitForJournalAsync(
            healId,
            observed => observed.AllHeard<DeliveryFailed>().Count == 1
                && observed.AllSaid<HealedPath>().Count == 1,
            "a heard DeliveryFailed and a said HealedPath on the heal router",
            ct);

        var heardFailure = Assert.IsType<DeliveryFailed>(healReading.HeardSingle<DeliveryFailed>().Body);
        Assert.Equal(failure, heardFailure);
        Assert.Equal(session.Id, healReading.HeardSingle<DeliveryFailed>().Metadata.Source);
        Assert.Equal(failedSaid.Position, healReading.HeardSingle<DeliveryFailed>().Metadata.Sequence);

        var healedSaid = healReading.SaidSingle<HealedPath>();
        var healed = Assert.IsType<HealedPath>(healedSaid.Body);
        Assert.Equal(failure.Fact, healed.FailedFact);
        Assert.Equal(failure.Receiver, healed.FailedReceiver);
        Assert.Equal(failure.Reason, healed.Reason);
        Assert.Equal("declared", healedSaid.DeliveryTo(observerId).Via);

        var observerReading = await WaitForJournalAsync(
            observerId,
            observed => observed.AllHeard<HealedPath>().Count == 1,
            "a heard HealedPath on the heal observer",
            ct);
        Assert.Equal(healId, observerReading.HeardSingle<HealedPath>().Metadata.Source);
        Assert.Equal(healed, Assert.IsType<HealedPath>(observerReading.HeardSingle<HealedPath>().Body));
    }
}
