using Orleans;

namespace DigitalBrain.Simulations;

[GenerateSerializer]
[Alias("db.test.ping")]
internal sealed record Ping : Synapse;

internal sealed class Echo : Neuron, IHandle<Ping>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
