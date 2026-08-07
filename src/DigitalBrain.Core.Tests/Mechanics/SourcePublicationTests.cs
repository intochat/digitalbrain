using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class SourcePublicationTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>()
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

    [Fact]
    public async Task RejectsUnregisteredIngressBeforeItCanBeRecorded()
    {
        const string name = "source-unregistered-ingress";
        var source = new NeuronId("digitalbrain.synapse-source", name);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PublishAsync(name, new MechanicsPulse(Echo: false), Cancellation));

        Assert.Contains("not registered as external ingress", failure.Message, StringComparison.Ordinal);
        Assert.Empty((await ReadAsync(source, cancellationToken: Cancellation)).Records);
    }

    [Fact]
    public async Task PublisherCannotUseAnIngressTypeOutsideItsIssuedCapability()
    {
        var workspace = OpenWorkspace(
            "workspace/limited-ingress",
            "limited-source",
            typeof(MechanicsStart));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.Publisher.PublishAsync(new MechanicsPulse(Echo: false), Cancellation));

        Assert.Contains("not permitted for this source channel", failure.Message, StringComparison.Ordinal);
    }
}
