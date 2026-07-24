using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Orleans;

namespace DigitalBrain.Quickstart;

[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "The quickstart models the public contract an external package author exposes.")]
public partial interface IGreeter : INeuron
{
    [Alias(nameof(Greet))]
    Task<string> Greet(string name);
}

[GenerateSerializer]
[Alias("quickstart.say-hello")]
internal sealed record SayHello : Synapse;

[GenerateSerializer]
[Alias("quickstart.greeted")]
internal sealed record Greeted : Synapse;

internal sealed class Greeter : Neuron, IGreeter, IHandle<SayHello>, IEmit<Greeted>
{
    public Task<string> Greet(string name)
        => Task.FromResult($"Hello, {name}!");

    public Task HandleAsync(SayHello synapse, CancellationToken cancellationToken)
        => EmitAsync(new Greeted());
}
