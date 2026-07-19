using Orleans;

namespace DigitalBrain.Simulations;

[GenerateSerializer]
[Alias("db.test.pong")]
internal sealed record Pong : Synapse;

internal sealed class Greeter : Neuron, IHandle<Ping>, IEmit<Pong>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken) => ReplyAsync(new Pong());
}

[GenerateSerializer]
[Alias("db.test.noticed")]
internal sealed record Noticed : Synapse;

internal sealed class Announcer : Neuron, IHandle<Ping>, IEmit<Noticed>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken) => EmitAsync(new Noticed());
}

internal sealed class Listener : Neuron, IHandle<Noticed>
{
    public Task HandleAsync(Noticed synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}

[GenerateSerializer]
[Alias("db.test.echoed")]
internal sealed record Echoed : Synapse;

internal sealed class Chatter : Neuron, IHandle<Echoed>, IEmit<Echoed>
{
    public Task HandleAsync(Echoed synapse, CancellationToken cancellationToken) => EmitAsync(new Echoed());
}

internal sealed class Relay : Neuron, IHandle<Ping>, IHandle<Pong>, IEmit<Ping>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
        => SendAsync(NeuronId.For<Greeter>(Id.Owner, "helper"), new Ping());

    public Task HandleAsync(Pong synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
