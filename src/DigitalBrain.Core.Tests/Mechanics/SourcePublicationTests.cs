using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class SourcePublicationTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterNeuron<MechanicsEmitter>("outboxemitter")
            .RegisterNeuron<MechanicsReceiver>("outboxreceiver");

    [Fact]
    public async Task PublicationCompletesAfterItsSourceOutputIsRecorded()
    {
        const string name = "source-recording";
        var source = new NeuronId("digitalbrain.synapse-source", name);

        await PublishAsync(name, new MechanicsStart(), Cancellation);

        var page = await ReadAsync(source, cancellationToken: Cancellation);
        var produced = Assert.Single(page.Records);
        Assert.Equal(JournalRecordDirection.Produced, produced.Direction);
        Assert.Equal(typeof(MechanicsStart).FullName, produced.SynapseKind);
        Assert.Equal(source, produced.Origin.Source);
        Assert.Equal(produced.Position, produced.Origin.Sequence);
        Assert.Null(produced.CausedBy);
    }
}
