using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// The full-turn safety net: one durable Send reaches the deterministic test responder and
// emits both the assistant reply and terminal lifecycle event.
[Collection(SimulationCollection.Name)]
public sealed class ChatTurnTests(SimulationFixture fixture)
{
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task SendRunsAWholeTurnThroughTheScriptedResponder()
    {
        var brain = fixture.Sim.Brain;
        var command = CommandId.New();
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");

        var accepted = await brain.GetGrainProxy<IChat>("main")
            .Send(new SendMessage(command, "hello", actor));

        Assert.Equal(command, accepted.CommandId);

        var chatId = NeuronId.For<IChat>(brain.Owner, "main");
        var terminal = await JournalWait.ForAsync(
            brain,
            chatId,
            JournalKind.Outgoing,
            delivery => delivery.Synapse is TurnLifecycle { Status: ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled },
            TurnTimeout);
        var lifecycle = Assert.IsType<TurnLifecycle>(terminal.Synapse);
        Assert.True(lifecycle.Status == ChatTurnStatus.Completed, lifecycle.Detail);

        var responded = await JournalWait.ForAsync(
            brain,
            chatId,
            JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded reply && reply.CommandId == command,
            TurnTimeout);
        Assert.Equal("Test assistant reply.", ((Responded)responded.Synapse).Text);

        Assert.Equal(accepted.TurnId, lifecycle.TurnId);

    }
}
