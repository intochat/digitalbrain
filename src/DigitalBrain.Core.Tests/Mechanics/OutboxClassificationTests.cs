using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class OutboxClassificationTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(RetrySeed).Assembly)
            .RegisterIngress<RetrySeed>()
            .RegisterNeuron<RetryEmitter>("classification-emitter")
            .RegisterNeuron<ForgedRejectionReceiver>("classification-receiver");

    [Fact]
    public async Task AHandlerCannotForgeATerminalDeliveryOutcomeWithItsExceptionMessage()
    {
        const string name = "forged-rejection";
        var emitter = new NeuronId("classification-emitter", name);
        ForgedRejectionReceiver.Reset();

        await PublishAsync(name, new RetrySeed(name), Cancellation);
        await ForgedRejectionReceiver.WaitForAttemptAsync(Cancellation);
        await DrainAsync(emitter, Cancellation);

        var page = await ReadAsync(emitter, cancellationToken: Cancellation);
        Assert.DoesNotContain(
            page.Records,
            record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(DeliveryFailed).FullName);
    }
}
