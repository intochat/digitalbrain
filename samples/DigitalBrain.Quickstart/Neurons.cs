using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Orleans;

namespace DigitalBrain.Quickstart;

[Alias("quickstart.greeter")]
internal interface IGreeter : INeuron
;

[GenerateSerializer]
[Alias("quickstart.say-hello")]
internal sealed record SayHello : Synapse;

[GenerateSerializer]
[Alias("quickstart.greeted")]
internal sealed record Greeted : Synapse;

internal sealed class Greeter : Neuron, IGreeter, IHandle<SayHello>, IEmit<Greeted>
{
    public Task HandleAsync(SayHello synapse, CancellationToken cancellationToken)
        => EmitAsync(new Greeted());
}
