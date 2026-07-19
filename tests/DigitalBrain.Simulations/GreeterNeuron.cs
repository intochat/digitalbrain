using Orleans;

namespace DigitalBrain.Simulations;

[GenerateSerializer]
[Alias("db.test.pong")]
internal sealed record Pong : Synapse;

internal sealed class Greeter : Neuron, IHandle<Ping>, IEmit<Pong>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken) => ReplyAsync(new Pong());
}

internal sealed class Relay : Neuron, IHandle<Ping>, IHandle<Pong>, IEmit<Ping>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
        => SendAsync(new NeuronId(nameof(Greeter), Id.Owner, "helper"), new Ping());

    public Task HandleAsync(Pong synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
