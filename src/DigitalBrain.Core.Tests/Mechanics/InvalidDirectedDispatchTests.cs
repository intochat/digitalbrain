using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class InvalidDirectedDispatchTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>()
            .RegisterNeuron<InvalidDirectedDispatchEmitter>("invalid-directed-emitter")
            .RegisterNeuron<InvalidDirectedDispatchTarget>("invalid-directed-target");

    [Fact]
    public async Task RejectedDirectOutputDoesNotBlockALaterValidInput()
    {
        const string name = "invalid-directed-route";
        var source = new NeuronId("digitalbrain.synapse-source", name);
        var emitter = new NeuronId("invalid-directed-emitter", name);

        await PublishAsync(name, new MechanicsStart(), Cancellation);
        await DrainAsync(source, Cancellation);

        await PublishAsync(name, new MechanicsStart(Echo: true), Cancellation);
        await DrainAsync(source, Cancellation);

        var page = await ReadAsync(emitter, cancellationToken: Cancellation);
        var received = page.Records.Single(record => record.Direction == JournalRecordDirection.Received
            && record.SynapseKind == typeof(MechanicsStart).FullName);
        Assert.True(received.Serialization.GetProperty("echo").GetBoolean());
        Assert.Contains(
            page.Records,
            record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MechanicsAudit).FullName);
        Assert.DoesNotContain(
            page.Records,
            record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(MechanicsPulse).FullName);

        var sourcePage = await ReadAsync(source, cancellationToken: Cancellation);
        Assert.DoesNotContain(
            sourcePage.Records,
            record => record.Direction == JournalRecordDirection.Produced
                && record.SynapseKind == typeof(DeliveryFailed).FullName);
    }
}
