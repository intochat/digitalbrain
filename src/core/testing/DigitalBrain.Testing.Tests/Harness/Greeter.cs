using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests.Harness;

internal sealed class Greeter :
    Neuron,
    IGreeter,
    IHandle<SayHello>,
    IEmit<Greeted>
{
    public Task HandleAsync(SayHello request, CancellationToken cancellationToken)
        => EmitAsync(new Greeted($"Hello, {request.Name}."));
}
