using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// Proves the IAgentToolSource seam end to end: a tool source registered in the silo's DI
// container reaches the model call as an AITool on ChatOptions.Tools. Runs its own
// BrainSimulation (rather than the shared SimulationFixture) because it needs a
// ConfigureSilo override that the shared fixture does not apply.
public sealed class AgentToolTests
{
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task AssistantOffersToolsFromRegisteredToolSources()
    {
        var capturing = new CapturingChatClient();

        await using var sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleManifest(
                [
                    typeof(DigitalBrain.Time.TimeModule),
                    typeof(DigitalBrain.Execution.ExecutionModule),
                    typeof(DigitalBrain.UI.UIModule),
                    typeof(DigitalBrain.AI.AIModule),
                    typeof(DigitalBrain.Memory.MemoryModule),
                ]),
            Configuration = new Dictionary<string, string?>
            {
                [DigitalBrainNames.Mode] = DigitalBrainNames.TestingMode,
            },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<IAgentToolSource, ProbeToolSource>();
                silo.Services.AddSingleton<IChatClient>(capturing);
            },
        });

        var brain = sim.Brain;
        var command = CommandId.New();
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");

        await brain.GetGrainProxy<IChat>("main").Send(new SendMessage(command, "hello", actor));

        var chatId = NeuronId.For<IChat>(brain.Owner, "main");
        await JournalWait.ForAsync(
            brain,
            chatId,
            JournalKind.Outgoing,
            delivery => delivery.Synapse is TurnLifecycle { Status: ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled },
            TurnTimeout);

        Assert.NotNull(capturing.LastOptions);
        Assert.NotNull(capturing.LastOptions!.Tools);
        Assert.Contains(capturing.LastOptions!.Tools!, tool => tool.Name == "probe_tool");
    }
}
