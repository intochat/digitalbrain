using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class InternalIngressEmissionTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>()
            .RegisterIngress<MechanicsPulse>()
            .RegisterNeuron<MechanicsEmitter>("internal-ingress-emitter")
            .RegisterNeuron<MechanicsReceiver>("internal-ingress-receiver");

    [Fact]
    public async Task ABehaviorCannotAuthorATypeReservedForExternalIngress()
    {
        const string name = "internal-ingress-forge";
        var source = new NeuronId("digitalbrain.synapse-source", name);
        var emitter = new NeuronId("internal-ingress-emitter", name);

        await PublishAsync(name, new MechanicsStart(), Cancellation);
        await DrainAsync(source, Cancellation);

        var page = await ReadAsync(emitter, cancellationToken: Cancellation);
        Assert.Empty(page.Records);
    }
}
