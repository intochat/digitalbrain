using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Orleans;

namespace DigitalBrain.Quickstart;

[Alias("quickstart.greeter")]
internal interface IGreeter : INeuron
{
    [Alias("SayHello")]
    Task SayHelloAsync();
}

[GenerateSerializer]
[Alias("quickstart.greeted")]
internal sealed record Greeted : Synapse;

internal sealed class Greeter : Neuron, IGreeter, IEmit<Greeted>
{
    public Task SayHelloAsync() => EmitAsync(new Greeted());
}
