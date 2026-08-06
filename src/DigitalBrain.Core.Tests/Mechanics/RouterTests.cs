namespace DigitalBrain;

public sealed class RouterTests
{
    [Fact]
    public void DeclaredListenersAtTheSenderNameAreTheReceiverSnapshot()
    {
        var catalog = Catalog.Build([typeof(RouterEmitter), typeof(RouterLeft), typeof(RouterRight)]);
        var router = new Router(catalog);

        var receivers = router.Resolve(new NeuronId("routeremitter", "locus"), typeof(RouterPulse));

        Assert.Equal(
            [new NeuronId("routerleft", "locus"), new NeuronId("routerright", "locus")],
            receivers.OrderBy(static receiver => receiver.Kind, StringComparer.Ordinal));
    }

    [Fact]
    public void EncodesNeuronIdentityWithoutDelimiterAmbiguity()
    {
        var id = new NeuronId("kind/with:delimiter", "name/with:delimiter");

        Assert.Equal(id, NeuronKey.Decode(NeuronKey.Encode(id)));
    }
}

public sealed record RouterStart : Synapse;

public sealed record RouterPulse : Synapse;

[GrainType("routeremitter")]
public sealed class RouterEmitter : Neuron, INeuron<RouterStart>
{
    public Task HandleAsync(RouterStart synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}

[GrainType("routerleft")]
public sealed class RouterLeft : Neuron, INeuron<RouterPulse>
{
    public Task HandleAsync(RouterPulse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}

[GrainType("routerright")]
public sealed class RouterRight : Neuron, INeuron<RouterPulse>
{
    public Task HandleAsync(RouterPulse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
