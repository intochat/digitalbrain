using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class OutboxTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>()
            .RegisterNeuron<MechanicsEmitter>("outboxemitter")
            .RegisterNeuron<MechanicsReceiver>("outboxreceiver");

    [Fact]
    public async Task RecordsTheProducedReceiverSnapshotBeforeTheReceiverReceivesTheSynapse()
    {
        const string name = "mechanics";
        var emitter = new NeuronId("outboxemitter", name);
        var receiver = new NeuronId("outboxreceiver", name);

        await PublishAsync(name, new MechanicsStart(), Cancellation);

        var senderPage = await WaitForJournalAsync(
            emitter,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MechanicsPulse).FullName),
            "a recorded outbox pulse",
            Cancellation);
        var produced = senderPage.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(MechanicsPulse).FullName);
        Assert.Contains(receiver, produced.DeliveryTargets);
        Assert.False(produced.Serialization.GetProperty("echo").GetBoolean());

        var receiverPage = await WaitForJournalAsync(
            receiver,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(MechanicsPulse).FullName),
            "a delivered outbox pulse",
            Cancellation);
        var received = receiverPage.Records.Single(record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(MechanicsPulse).FullName);
        Assert.Equal(emitter, received.Origin.Source);
        Assert.Equal(produced.Position, received.Origin.Sequence);
    }

    [Fact]
    public async Task PublicationReturnsAfterRecordingInsteadOfWaitingForAChildOutbox()
    {
        const string name = "cycle";
        var emitter = new NeuronId("outboxemitter", name);

        await PublishAsync(name, new MechanicsStart(Echo: true), Cancellation)
            .WaitAsync(TimeSpan.FromSeconds(2), Cancellation);

        _ = await WaitForJournalAsync(
            emitter,
            page => page.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(MechanicsEcho).FullName),
            "an asynchronously delivered outbox echo",
            Cancellation);
    }

    [Fact]
    public async Task RecordsProducedSynapsesWithNoDeclaredReceiver()
    {
        const string name = "audit";
        var emitter = new NeuronId("outboxemitter", name);

        await PublishAsync(name, new MechanicsStart(Audit: true), Cancellation);

        var page = await WaitForJournalAsync(
            emitter,
            journal => journal.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MechanicsAudit).FullName),
            "a recorded zero-target production",
            Cancellation);
        var produced = page.Records.Single(record => record.Direction == JournalRecordDirection.Produced
            && record.SynapseKind == typeof(MechanicsAudit).FullName);
        Assert.Empty(produced.DeliveryTargets);
    }
}
