using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class OriginAuthorityTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>()
            .RegisterNeuron<MechanicsEmitter>("origin-authority-emitter")
            .RegisterNeuron<MechanicsReceiver>("origin-authority-receiver");

    [Fact]
    public async Task PreservesWhetherAnInputCameFromExternalIngressOrAnotherBehavior()
    {
        const string name = "origin-authority";
        var emitter = new NeuronId("origin-authority-emitter", name);
        var receiver = new NeuronId("origin-authority-receiver", name);

        await PublishAsync(name, new MechanicsStart(), Cancellation);

        var emitterPage = await WaitForJournalAsync(
            emitter,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MechanicsPulse).FullName),
            "the behavior-authored pulse",
            Cancellation);
        var externalStart = emitterPage.Records.Single(record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(MechanicsStart).FullName);
        Assert.True(externalStart.Origin.IsExternalIngress);

        var receiverPage = await WaitForJournalAsync(
            receiver,
            observed => observed.Records.Any(record => record.Direction == JournalRecordDirection.Received
                && record.SynapseKind == typeof(MechanicsPulse).FullName),
            "the behavior-authored pulse arriving at the receiver",
            Cancellation);
        var internalPulse = receiverPage.Records.Single(record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(MechanicsPulse).FullName);
        Assert.False(internalPulse.Origin.IsExternalIngress);
    }
}
