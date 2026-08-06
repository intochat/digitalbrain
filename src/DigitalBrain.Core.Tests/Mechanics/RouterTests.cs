using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class RouterTests
{
    [Fact]
    public void DeclaredListenersAtTheSenderNameAreTheReceiverSnapshot()
    {
        var catalog = new DigitalBrainComposition()
            .RegisterVocabulary(typeof(MechanicsPulse).Assembly)
            .RegisterNeuron<MechanicsEmitter>("routeremitter")
            .RegisterNeuron<MechanicsReceiver>("routerleft")
            .RegisterNeuron<MechanicsReceiver>("routerright")
            .Seal();
        var router = new Router(catalog);

        var receivers = router.Resolve(new NeuronId("routeremitter", "locus"), typeof(MechanicsPulse));

        Assert.Equal(
            [new NeuronId("routerleft", "locus"), new NeuronId("routerright", "locus")],
            receivers.OrderBy(static receiver => receiver.Kind, StringComparer.Ordinal));
    }

    [Fact]
    public void EncodesLogicalIdentityWithoutDelimiterAmbiguity()
    {
        var id = new NeuronId("kind/with:delimiter", "name/with:delimiter");

        Assert.Equal(id, NeuronKey.Decode(NeuronKey.Encode(id)));
    }
}
