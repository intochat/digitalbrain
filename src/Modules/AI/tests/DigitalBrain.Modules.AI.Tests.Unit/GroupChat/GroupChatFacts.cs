using DigitalBrain.AI;
using DigitalBrain.AI.GroupChat;
using DigitalBrain.AI.GroupChat.Signals;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.Scripted;
using Xunit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class GroupChatFacts
{
    private static readonly GroupChatSetup LunaAndGemma = new(
        [new("Luna", IScriptedLLM.ModelPrefix + "luna"), new("Gemma", IScriptedLLM.ModelPrefix + "gemma")],
        Brief: "Brainstorm product ideas.",
        MaxRounds: 3);

    [Fact]
    public async Task ParticipantsTakeTurnsAndConcludeWhenTheyAgree()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        await brain.Get<IScriptedLLM>("luna").Script(["Idea: a smart dog bowl.", "AGREE: the smart bowl, with a feeding log.", "Final: a smart dog bowl that logs feeding."]);
        await brain.Get<IScriptedLLM>("gemma").Script(["Building on the bowl: add a feeding log.", "AGREE: bowl plus log."]);

        var chat = brain.Get<IGroupChat>("chat-1");
        await chat.Configure(LunaAndGemma);
        await using var spoke = await brain.Observe<GroupChatSpoke>(chat, ct);
        await using var concluded = await brain.Observe<GroupChatConcluded>(chat, ct);
        var state = await chat.Ask("Name one product idea for dog owners");

        Assert.Equal(["Luna", "Gemma", "Luna", "Gemma"], state.Turns.Select(turn => turn.Speaker));
        Assert.Equal([1, 1, 2, 2], state.Turns.Select(turn => turn.Round));
        Assert.Equal(GroupChatStatus.Concluded, state.Status);
        Assert.True(state.Agreed);
        Assert.Equal("Final: a smart dog bowl that logs feeding.", state.Answer);
        Assert.Contains("Luna: Idea: a smart dog bowl.", (await brain.Get<IScriptedLLM>("gemma").Prompts())[0]);
        Assert.Equal("Luna", (await spoke.NextAsync(ct: ct)).Turn.Speaker);
        var conclusion = await concluded.NextAsync(ct: ct);
        Assert.Equal(2, conclusion.Rounds);
        Assert.Equal(state.Answer, conclusion.Answer);
        await brain.DeactivateAsync(chat, ct);
        var restored = await chat.Read();
        Assert.Equal(state.Turns, restored.Turns);
        Assert.Equal(LunaAndGemma.Participants, restored.Setup!.Participants);
        Assert.Equal(state.Answer, restored.Answer);
        Assert.Equal(GroupChatStatus.Concluded, restored.Status);
    }

    [Fact]
    public async Task DiscussionStopsAtMaxRoundsWithoutAgreement()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        await brain.Get<IScriptedLLM>("luna").Script(["A", "B", "C", "Final: A."]);
        await brain.Get<IScriptedLLM>("gemma").Script(["not A", "not B", "not C"]);

        var chat = brain.Get<IGroupChat>("chat-2");
        await chat.Configure(LunaAndGemma);
        var state = await chat.Ask("Pick one");

        Assert.Equal(6, state.Turns.Length);
        Assert.False(state.Agreed);
        Assert.Equal("Final: A.", state.Answer);
    }

    [Fact]
    public async Task AFailingParticipantLeavesTheChatFailedWithTheTurnsSoFar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        await brain.Get<IScriptedLLM>("luna").Script(["A"]);
        await brain.Get<IScriptedLLM>("gemma").Script([]);

        var chat = brain.Get<IGroupChat>("chat-5");
        await chat.Configure(LunaAndGemma);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chat.Ask("Pick one"));

        var state = await chat.Read();
        Assert.Equal(GroupChatStatus.Failed, state.Status);
        Assert.Equal("Luna", Assert.Single(state.Turns).Speaker);
    }

    [Fact]
    public async Task AskingBeforeConfiguringIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IGroupChat>("chat-3").Ask("anything"));
    }

    [Fact]
    public async Task UnknownModelsAreRejectedAtConfiguration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        await Assert.ThrowsAsync<ArgumentException>(() => brain.Get<IGroupChat>("chat-4")
            .Configure(new([new("Ghost", "INoSuchModel"), new("Gemma", nameof(IGemma4))])));
    }
}
