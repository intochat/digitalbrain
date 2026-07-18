using Brain.Contracts;
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

    [Fact]
    public async Task Resume_provider_failure_does_not_retry_participant_via_fresh_rebuild()
    {
        var alpha = new ScriptedChatClient("alpha-reply");
        var beta = new ScriptedChatClient("beta-reply");
        AIAgent first = new ChatClientAgent(alpha, name: "gpt56");
        AIAgent second = new ChatClientAgent(beta, name: "grok45");
        var engine = new OneStepGroupChatEngine();

        var firstStep = await engine.AdvanceAsync(
            [new ChatMessage(ChatRole.User, "topic")],
            participantCursor: 0,
            first,
            second,
            checkpointSessionId: null,
            checkpointId: null,
            checkpointJson: null);

        Assert.Equal(1, alpha.InvocationCount);
        Assert.Equal(0, beta.InvocationCount);
        Assert.False(string.IsNullOrWhiteSpace(firstStep.CheckpointJson));

        beta.FailNextWith("provider boom secret-token-xyz");
        alpha.Reset();
        beta.Reset();
        beta.FailNextWith("provider boom secret-token-xyz");

        var transcript = new List<ChatMessage>
        {
            new(ChatRole.User, "topic"),
            firstStep.ParticipantResponses[0]
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            engine.AdvanceAsync(
                transcript,
                participantCursor: 1,
                first,
                second,
                firstStep.Checkpoint.SessionId,
                firstStep.Checkpoint.CheckpointId,
                firstStep.CheckpointJson));

        Assert.True(
            ex is BrainException || ex.InnerException is BrainException || ex is InvalidOperationException,
            $"Unexpected exception type: {ex.GetType().FullName}: {ex.Message}");
        Assert.Equal(1, beta.InvocationCount);
        Assert.Equal(0, alpha.InvocationCount);
    }

    [Fact]
    public async Task Unusable_checkpoint_falls_back_before_participant_call()
    {
        var alpha = new ScriptedChatClient("alpha-rebuild");
        var beta = new ScriptedChatClient("beta-rebuild");
        AIAgent first = new ChatClientAgent(alpha, name: "gpt56");
        AIAgent second = new ChatClientAgent(beta, name: "grok45");
        var engine = new OneStepGroupChatEngine();

        var result = await engine.AdvanceAsync(
            [new ChatMessage(ChatRole.User, "topic")],
            participantCursor: 0,
            first,
            second,
            checkpointSessionId: "session-missing",
            checkpointId: "checkpoint-missing",
            checkpointJson: "{}");

        Assert.Single(result.ParticipantResponses);
        Assert.Equal("alpha-rebuild", result.ParticipantResponses[0].Text);
        Assert.False(result.UsedCheckpointResume);
        Assert.Equal(1, alpha.InvocationCount);
        Assert.Equal(0, beta.InvocationCount);
    }
}
