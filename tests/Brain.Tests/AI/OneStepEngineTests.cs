using DigitalBrain.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Brain.Tests.AI;

[Collection(AiTestCollection.Name)]
public sealed class OneStepEngineTests
{
    [Fact]
    public async Task Engine_one_step_returns_response_and_checkpoint()
    {
        var alpha = new ScriptedChatClient("alpha-reply");
        var beta = new ScriptedChatClient("beta-reply");
        AIAgent first = new ChatClientAgent(alpha, name: "gpt56");
        AIAgent second = new ChatClientAgent(beta, name: "grok45");
        var engine = new OneStepGroupChatEngine();

        var result = await engine.AdvanceAsync(
            [new ChatMessage(ChatRole.User, "topic")],
            participantCursor: 0,
            first,
            second,
            checkpointSessionId: null,
            checkpointId: null,
            checkpointJson: null);

        Assert.Single(result.ParticipantResponses);
        Assert.Equal("alpha-reply", result.ParticipantResponses[0].Text);
        Assert.False(string.IsNullOrWhiteSpace(result.Checkpoint.CheckpointId));
        Assert.False(string.IsNullOrWhiteSpace(result.CheckpointJson));
        Assert.Equal(1, alpha.InvocationCount);
        Assert.Equal(0, beta.InvocationCount);
    }
}
