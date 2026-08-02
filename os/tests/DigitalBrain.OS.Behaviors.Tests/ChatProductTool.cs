using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.OS.AgentTools;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Behaviors.Tests;

public sealed class ChatProductTool(OSBehaviorsFixture fixture)
{
    private const int TestTimeout = 60_000;

    [Fact(Timeout = TestTimeout, DisplayName = "send_chat_message returns the assistant response for its own command id")]
    public async Task SendChatMessageReturnsTheResponseForItsOwnCommandId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply("Hello from DigitalBrain.");
        var tools = new DigitalBrainMcpTools(test.Client, test.Cluster.Client);
        var commandId = CommandId.New();

        var result = await tools.SendChatMessageAsync(
            "hi",
            commandId.ToString(),
            "main",
            timeoutSeconds: 10,
            cancellationToken: cancellationToken);

        Assert.Equal("main", result.Chat);
        Assert.Equal("Hello from DigitalBrain.", result.Response);
        Assert.Equal(commandId.ToString(), result.CommandId);
        Assert.NotEqual(Guid.Empty.ToString("N"), result.CorrelationId);
        Assert.True(result.Sequence > 0);

        await test.Neuron<IChat>("main").RestartHostAsync(cancellationToken);
        var retried = await tools.SendChatMessageAsync(
            "hi",
            commandId.ToString(),
            "main",
            timeoutSeconds: 10,
            cancellationToken: cancellationToken);

        Assert.Equal(result, retried);

        var transcript = await test.Client.GetGrainProxy<IChat>("main").Read();
        Assert.Collection(
            transcript.Turns,
            user =>
            {
                Assert.True(user.FromUser);
                Assert.Equal("hi", user.Text);
            },
            assistant =>
            {
                Assert.False(assistant.FromUser);
                Assert.Equal(result.Response, assistant.Text);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.SendChatMessageAsync(
                "different text",
                commandId.ToString(),
                "main",
                timeoutSeconds: 10,
                cancellationToken: cancellationToken));
    }
}
