using DigitalBrain.Chat;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Mcp.Tests;

public sealed class ChatProductTool(OSMcpFixture fixture)
{
    private const int TestTimeout = 60_000;

    [Fact(Timeout = TestTimeout, DisplayName = "send_chat_message returns the correlated assistant response")]
    public async Task SendChatMessageReturnsCorrelatedAssistantResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply("Hello from DigitalBrain.");
        var tools = new DigitalBrainMcpTools(test.Client, test.Cluster.Client);

        var result = await tools.SendChatMessageAsync("hi", "main", timeoutSeconds: 10, cancellationToken);

        Assert.Equal("main", result.Chat);
        Assert.Equal("Hello from DigitalBrain.", result.Response);
        Assert.NotEqual(Guid.Empty.ToString("N"), result.CommandId);
        Assert.NotEqual(Guid.Empty.ToString("N"), result.CorrelationId);
        Assert.True(result.Sequence > 0);

        var transcript = await test.Client.Get<IChat>("main").Read();
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
    }
}
