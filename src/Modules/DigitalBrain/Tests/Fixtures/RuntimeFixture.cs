using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

internal static class RuntimeFixture
{
    internal static Task<BrainSimulation> StartAsync()
        => BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton(new CounterFixtureState()),
        });
}
