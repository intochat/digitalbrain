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
                [
                    typeof(DigitalBrain.Time.StartTimer).Assembly,
                    typeof(DigitalBrain.Chat.SendMessage).Assembly,
                    typeof(DigitalBrain.AI.IAssistant).Assembly,
                    typeof(DigitalBrain.Memory.IVectorMemory).Assembly,
                ],
                [
                    typeof(DigitalBrain.Time.TimerNeuron).Assembly,
                    typeof(DigitalBrain.UI.UiModule).Assembly,
                    typeof(DigitalBrain.AI.AIModule).Assembly,
                    typeof(DigitalBrain.Memory.MemoryModule).Assembly,
                    typeof(SimulationFixture).Assembly,
                ]),
            Configuration = new Dictionary<string, string?>
            {
                // Boot the AI module in testing mode: every model key resolves the
                // corpus-scripted mock (AITestingClients), no Ollama/OpenAI containers.
                [DigitalBrain.Abstractions.DigitalBrainNames.Mode] =
                    DigitalBrain.Abstractions.DigitalBrainNames.TestingMode,
                ["DigitalBrain:AI:Corpus:Path"] = Path.Combine(AppContext.BaseDirectory, "corpus"),
            },
        });

    public async ValueTask DisposeAsync() => await Sim.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SimulationCollection : ICollectionFixture<SimulationFixture>
{
    public const string Name = "simulation";
}
