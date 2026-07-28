using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Client;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Constructed by the generic host DI container via AddHostedService.")]
internal sealed class DigitalBrainActivationHostedService(IDigitalBrain brain) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => brain.ActivateAsync();

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
