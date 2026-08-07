using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class DirectedDispatchTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>()
            .RegisterNeuron<DirectedDispatchEmitter>("directing-emitter")
            .RegisterNeuron<DirectedDispatchReceiver>("directing-receiver");

    [Fact]
    public async Task RecordsAndDeliversTheDeclaredReceiverInsteadOfSenderNameBroadcast()
    {
        const string origin = "origin";
        var emitter = new NeuronId("directing-emitter", origin);
        var receiver = new NeuronId("directing-receiver", "destination");

        await PublishAsync(origin, new MechanicsStart(), Cancellation);

        var senderPage = await WaitForJournalAsync(
            emitter,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MechanicsPulse).FullName),
            "a recorded directed pulse",
            Cancellation);
        var produced = senderPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(MechanicsPulse).FullName);
        Assert.Equal([receiver], produced.DeliveryTargets);

        var receiverPage = await WaitForJournalAsync(
            receiver,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(MechanicsPulse).FullName),
            "the declared directed pulse",
            Cancellation);
        var received = receiverPage.Records.Single(record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(MechanicsPulse).FullName);
        Assert.Equal(emitter, received.Origin.Source);
        Assert.Equal(produced.Position, received.Origin.Sequence);
    }

    [Fact]
    public async Task DirectedDeliveryCannotReachASameNamedReceiverInAnotherWorkspace()
    {
        const string origin = "workspace-origin";
        var receiver = new NeuronId("directing-receiver", "destination");
        var left = OpenWorkspace("workspace/left", origin, typeof(MechanicsStart));
        var right = OpenWorkspace("workspace/right", origin, typeof(MechanicsStart));

        await left.Publisher.PublishAsync(new MechanicsStart(), Cancellation);

        _ = await WaitForJournalAsync(
            left,
            receiver,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(MechanicsPulse).FullName),
            "the directed pulse in its source workspace",
            Cancellation);

        var otherWorkspace = await ReadAsync(right, receiver, cancellationToken: Cancellation);

        Assert.Empty(otherWorkspace.Records);
        Assert.Equal(0, otherWorkspace.JournalEndPosition);
    }
}
