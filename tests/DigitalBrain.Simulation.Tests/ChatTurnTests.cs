using DigitalBrain.Product.Identity;
using DigitalBrain.Abstractions;
using DigitalBrain.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Chat;
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
        var chatName = fixture.Sim.UniqueId("chat");
        var command = CommandId.New();
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");
        var cancellationToken = TestContext.Current.CancellationToken;

        var accepted = await brain.Get<IChat>(chatName)
            .RequestAsync(new SendMessage(command, "hello", actor), cancellationToken);

        Assert.Equal(command, accepted.CommandId);

        var lifecycle = await ChatTurnDriver.AwaitCompletedTurnAsync(brain, chatName);

        var responded = await JournalWait.ForAsync(
            brain.Get<IChat>(chatName),
            JournalKind.Outgoing,
            delivery => delivery.Signal is Responded reply && reply.CommandId == command,
            TurnTimeout,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Test assistant reply.", ((Responded)responded.Signal).Text);

        Assert.Equal(accepted.TurnId, lifecycle.TurnId);

    }

    [Fact]
    public async Task SendSetsChatActiveExecutionIdOnTheExecutionSpine()
    {
        var brain = fixture.Sim.Brain;
        var chatName = fixture.Sim.UniqueId("active-execution");
        var command = CommandId.New();
        var actor = new ActorContext(new PrincipalId(Guid.NewGuid()), "owner");
        var chat = brain.Get<IChat>(chatName);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Null((await chat.RequestAsync(new ReadActiveExecution(), cancellationToken)).ExecutionId);

        var accepted = await chat.RequestAsync(new SendMessage(command, "hello execution", actor), cancellationToken);

        await ChatTurnDriver.AwaitCompletedTurnAsync(brain, chatName);

        var active = (await chat.RequestAsync(new ReadActiveExecution(), cancellationToken)).ExecutionId;
        Assert.NotNull(active);

        var projection = await brain.Get<IExecution>(active!.Value.ToString())
            .RequestAsync(new ReadExecution(), cancellationToken);
        Assert.Equal(active.Value, projection.ExecutionId);
        Assert.Equal(ExecutionStatus.Completed, projection.Status);
        Assert.Equal(accepted.TurnId.Value, Assert.IsType<ChatTurnWorkload>(projection.Workload).TurnId);
    }

    [Fact]
    public async Task KitCardOfferLandsInTheChatJournalAsARespondedCard()
    {
        var brain = fixture.Sim.Brain;
        var chatName = fixture.Sim.UniqueId("chat");
        var chat = brain.Get<IChat>(chatName);
        var cancellationToken = TestContext.Current.CancellationToken;

        await chat.SendAsync(
            new KitCardOffer(KitCardKinds.Chart, "chart-abc12345", "Quarterly sales"),
            cancellationToken);

        var transcript = (await chat.RequestAsync(new ReadTranscriptRequest(chatName), cancellationToken)).Transcript;
        Assert.Contains(transcript.Turns, turn => !turn.FromUser && turn.Text == "Quarterly sales");

        var responded = await JournalWait.ForAsync(
            brain.Get<IChat>(chatName),
            JournalKind.Outgoing,
            delivery => delivery.Signal is Responded reply && reply.Text == "Quarterly sales",
            TurnTimeout,
            cancellationToken: TestContext.Current.CancellationToken);

        var cards = Assert.IsType<Responded>(responded.Signal).Cards;
        Assert.NotNull(cards);
        var card = Assert.Single(cards);
        Assert.Equal(KitCardKinds.Chart, card.Kind);
        Assert.Equal("chart-abc12345", card.Name);
        Assert.Equal("Quarterly sales", card.Caption);
    }

    [Fact]
    public async Task ChatRefusesAKitCardWithABlankCaption()
    {
        var chat = fixture.Sim.Brain.Get<IChat>(fixture.Sim.UniqueId("chat"));

        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => chat.SendAsync(
            new KitCardOffer(KitCardKinds.Chart, "chart-abc12345", "   "),
            TestContext.Current.CancellationToken));
    }
}
