using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests.Harness;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class Greeter :
    Neuron,
    IGreeter,
    IHandle<SayHello>,
    IEmit<Greeted>
{
    public Task Greet(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return EmitAsync(new Greeted($"Hello, {name}."));
    }

    public Task HandleAsync(SayHello request, CancellationToken cancellationToken)
        => EmitAsync(new Greeted($"Hello, {request.Name}."));
}
