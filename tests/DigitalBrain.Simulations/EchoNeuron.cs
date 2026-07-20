using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Simulations;

[GenerateSerializer]
[Alias("db.test.ping")]
internal sealed record Ping : Synapse;

[Alias("db.test.echo-probe")]
internal interface IEchoProbe : INeuron
{
    [Alias("Poke")]
    Task PokeAsync();
}

internal sealed class Echo : Neuron, IHandle<Ping>, IEchoProbe
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PokeAsync() => Task.CompletedTask;
}
