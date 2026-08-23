using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Execution;
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

    [Fact]
    public async Task SendSetsChatActiveExecutionIdOnTheExecutionSpine()
    {
        var brain = fixture.Sim.Brain;
        var command = CommandId.New();
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");
        var chat = brain.GetGrainProxy<IChat>("active-execution");

        Assert.Null(await chat.ReadActiveExecution());

        var accepted = await chat.Send(new SendMessage(command, "hello execution", actor));

        var chatId = NeuronId.For<IChat>(brain.Owner, "active-execution");
        var terminal = await JournalWait.ForAsync(
            brain,
            chatId,
            JournalKind.Outgoing,
            delivery => delivery.Synapse is TurnLifecycle { Status: ChatTurnStatus.Completed or ChatTurnStatus.Failed or ChatTurnStatus.Cancelled },
            TurnTimeout);
        var lifecycle = Assert.IsType<TurnLifecycle>(terminal.Synapse);
        Assert.True(lifecycle.Status == ChatTurnStatus.Completed, lifecycle.Detail);

        var active = await chat.ReadActiveExecution();
        Assert.NotNull(active);

        var execution = brain.GetGrainProxy<IExecution>(active!.Value.ToString());
        var projection = await execution.Read();
        Assert.Equal(active.Value, projection.ExecutionId);
        Assert.Equal(ExecutionStatus.Completed, projection.Status);
        Assert.Equal(accepted.TurnId.Value, Assert.IsType<ChatTurnWorkload>(projection.Workload).TurnId);
    }

    [Fact]
    public async Task KitCardOfferLandsInTheChatJournalAsARespondedCard()
    {
        var brain = fixture.Sim.Brain;
        var chat = brain.GetGrainProxy<IChat>("main");

        await chat.HandleAsync(
            new KitCardOffer(KitCardKinds.Chart, "chart-abc12345", "Quarterly sales"),
            CancellationToken.None);

        var transcript = await chat.Read();
        Assert.Contains(transcript.Turns, turn => !turn.FromUser && turn.Text == "Quarterly sales");

        var chatId = NeuronId.For<IChat>(brain.Owner, "main");
        var responded = await JournalWait.ForAsync(
            brain,
            chatId,
            JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded reply && reply.Text == "Quarterly sales",
            TurnTimeout);

        var cards = Assert.IsType<Responded>(responded.Synapse).Cards;
        Assert.NotNull(cards);
        var card = Assert.Single(cards);
        Assert.Equal(KitCardKinds.Chart, card.Kind);
        Assert.Equal("chart-abc12345", card.Name);
        Assert.Equal("Quarterly sales", card.Caption);
    }

    [Fact]
    public async Task ChatRefusesAKitCardWithABlankCaption()
    {
        var chat = fixture.Sim.Brain.GetGrainProxy<IChat>("main");

        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => chat.HandleAsync(
            new KitCardOffer(KitCardKinds.Chart, "chart-abc12345", "   "),
            CancellationToken.None));
    }
}
