using DigitalBrain.DevTools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Tests.DevTools;

public sealed class DigitalBrainConversationChatClientTests
{
    [Theory]
    [InlineData(ConversationRole.Fast)]
    [InlineData(ConversationRole.Balanced)]
    [InlineData(ConversationRole.Reasoning)]
    public async Task Role_proxy_submits_the_latest_user_turn_with_explicit_owner_and_conversation(
        ConversationRole role)
    {
        BrainOwnerId? observedOwner = null;
        ConversationRole? observedRole = null;
        ConversationId? observedConversation = null;
        string? observedText = null;
        var resultTurn = ConversationTurnId.New();
        using var client = new DigitalBrainConversationChatClient(
            new BrainOwnerId("owner-a"),
            role,
            (owner, selectedRole, conversation, _, text, _) =>
            {
                observedOwner = owner;
                observedRole = selectedRole;
                observedConversation = conversation;
                observedText = text;
                return Task.FromResult(new ConversationTurnResult(
                    resultTurn,
                    selectedRole,
                    $"{selectedRole}:{text}",
                    1));
            });

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "old"),
                new ChatMessage(ChatRole.Assistant, "answer"),
                new ChatMessage(ChatRole.User, "latest")
            ],
            new ChatOptions { ConversationId = "conversation-a" });

        Assert.Equal(new BrainOwnerId("owner-a"), observedOwner);
        Assert.Equal(role, observedRole);
        Assert.Equal(new ConversationId("conversation-a"), observedConversation);
        Assert.Equal("latest", observedText);
        Assert.Equal($"{role}:latest", response.Text);
        Assert.Equal("conversation-a", response.ConversationId);
        Assert.Equal(resultTurn.ToString(), response.ResponseId);
    }

    [Fact]
    public async Task First_turn_creates_a_bounded_conversation_id_that_can_be_reused()
    {
        var conversations = new List<ConversationId>();
        using var client = new DigitalBrainConversationChatClient(
            new BrainOwnerId("owner-a"),
            ConversationRole.Fast,
            (_, role, conversation, turnId, _, _) =>
            {
                conversations.Add(conversation);
                return Task.FromResult(new ConversationTurnResult(
                    turnId,
                    role,
                    "ok",
                    conversations.Count));
            });

        var first = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")]);
        var second = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "again")],
            new ChatOptions { ConversationId = first.ConversationId });

        Assert.False(string.IsNullOrWhiteSpace(first.ConversationId));
        Assert.True(first.ConversationId!.Length <= ConversationId.MaximumLength);
        Assert.Equal(conversations[0], conversations[1]);
        Assert.Equal(first.ConversationId, second.ConversationId);
    }

    [Fact]
    public async Task Streaming_proxy_emits_one_durable_final_update()
    {
        using var client = new DigitalBrainConversationChatClient(
            new BrainOwnerId("owner-a"),
            ConversationRole.Reasoning,
            (_, role, _, turnId, _, _) => Task.FromResult(
                new ConversationTurnResult(turnId, role, "final", 1)));
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "question")],
            new ChatOptions { ConversationId = "conversation-a" }))
        {
            updates.Add(update);
        }

        var final = Assert.Single(updates);
        Assert.Equal("final", final.Text);
        Assert.Equal("conversation-a", final.ConversationId);
        Assert.Equal(ChatFinishReason.Stop, final.FinishReason);
    }

    [Fact]
    public async Task Missing_user_input_fails_closed()
    {
        using var client = new DigitalBrainConversationChatClient(
            new BrainOwnerId("owner-a"),
            ConversationRole.Fast,
            (_, role, _, turnId, _, _) => Task.FromResult(
                new ConversationTurnResult(turnId, role, "unused", 1)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync(
                [new ChatMessage(ChatRole.Assistant, "not user input")]));

        Assert.Contains("user", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
