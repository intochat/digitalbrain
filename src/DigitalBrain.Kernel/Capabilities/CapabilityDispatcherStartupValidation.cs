using Microsoft.Extensions.Hosting;
namespace DigitalBrain.Kernel.Capabilities;

internal sealed class CapabilityDispatcherStartupValidation(ICapabilityDispatcher dispatcher) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = dispatcher;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
