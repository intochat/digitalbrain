using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class SimulationFixture : IAsyncLifetime
{
    private BrainSimulation? _sim;

    public BrainSimulation Sim => _sim ?? throw new InvalidOperationException("Simulation has not started.");

    public async ValueTask InitializeAsync()
        => _sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest(
                [
                    typeof(DigitalBrain.Time.TimeModule),
                    typeof(DigitalBrain.UI.UIModule),
                    typeof(DigitalBrain.AI.AIModule),
                    typeof(DigitalBrain.Memory.MemoryModule),
                    typeof(DigitalBrain.Execution.ExecutionModule),
                    typeof(DigitalBrain.Google.GoogleModule),
                    typeof(DigitalBrain.Salesforce.SalesforceModule),
                    typeof(DigitalBrain.SmartPrompt.SmartPromptModule),
                ]),
            Configuration = new Dictionary<string, string?>
            {
                // Boot the AI module in testing mode: every model key resolves the
                // deterministic test responder, no Ollama/OpenAI containers.
                [DigitalBrain.Abstractions.DigitalBrainNames.Mode] =
                    DigitalBrain.Abstractions.DigitalBrainNames.TestingMode,
            },
        });

    public async ValueTask DisposeAsync()
    {
        if (_sim is not null)
        {
            await _sim.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SimulationCollection : ICollectionFixture<SimulationFixture>
{
    public const string Name = "simulation";
}
