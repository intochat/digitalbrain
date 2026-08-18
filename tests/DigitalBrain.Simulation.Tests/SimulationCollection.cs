using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SimulationFixture : IAsyncLifetime
{
    public BrainSimulation Sim { get; private set; } = null!;

    public async ValueTask InitializeAsync()
        => Sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleAssemblies(
                [typeof(DigitalBrain.Time.StartTimer).Assembly, typeof(DigitalBrain.Chat.SendMessage).Assembly],
                [
                    typeof(DigitalBrain.Time.TimerNeuron).Assembly,
                    typeof(DigitalBrain.UI.UiModule).Assembly,
                    typeof(SimulationFixture).Assembly,
                ]),
        });

    public async ValueTask DisposeAsync() => await Sim.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SimulationCollection : ICollectionFixture<SimulationFixture>
{
    public const string Name = "simulation";
}
