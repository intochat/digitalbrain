using DigitalBrain.Abstractions.Descriptors;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BrainSimulationFacts
{
    [Fact]
    public async Task StartsAndRestartsTheBrainRuntime()
    {
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
        });

        var originalInvoker = simulation.SiloServices.GetRequiredService<INeuronInvoker>();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await simulation.RestartSiloAsync(timeout.Token);

        var restartedInvoker = simulation.SiloServices.GetRequiredService<INeuronInvoker>();
        Assert.NotSame(originalInvoker, restartedInvoker);
    }
}
