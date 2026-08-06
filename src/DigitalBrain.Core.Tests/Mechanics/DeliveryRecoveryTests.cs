using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class DeliveryRecoveryTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(RetrySeed).Assembly)
            .RegisterNeuron<RetryEmitter>("retry-emitter")
            .RegisterNeuron<RetryReceiver>("retry-receiver");

    [Fact]
    public async Task ContinuesARecordedDeliveryAfterTheSenderIsReactivated()
    {
        const string name = "delivery-reload";
        var emitter = new NeuronId("retry-emitter", name);
        var receiver = new NeuronId("retry-receiver", name);
        RetryReceiver.Reset(name);

        await PublishAsync(name, new RetrySeed(name), Cancellation);
        await RetryReceiver.WaitForFirstAttemptAsync(name, Cancellation);
        await DeactivateAsync([emitter], Cancellation);

        RetryReceiver.AllowDelivery(name);
        await DrainAsync(emitter, Cancellation);

        _ = await WaitForJournalAsync(
            receiver,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(RetryPulse).FullName),
            "the redelivered retry pulse",
            Cancellation);
        Assert.True(RetryReceiver.AttemptsFor(name) >= 2);
    }
}
