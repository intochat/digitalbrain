using Orleans;

namespace DigitalBrain.Quickstart;

[GenerateSerializer]
[Alias("quickstart.hello")]
internal sealed record Hello : Synapse;

[GenerateSerializer]
[Alias("quickstart.greeted")]
internal sealed record Greeted : Synapse;

internal sealed class Greeter : Neuron, IHandle<Hello>, IEmit<Greeted>
{
    public Task HandleAsync(Hello synapse, CancellationToken cancellationToken) => ReplyAsync(new Greeted());
}
