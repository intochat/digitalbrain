using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.OS.AgentTools;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Assistant.Tests;

public sealed class ChatTranscriptTool(OSBehaviorsFixture fixture)
{
    [Fact(DisplayName =
        "read_chat_transcript keeps its frozen tool name")]
    public void ToolNameIsFrozen()
        => Assert.Equal("read_chat_transcript", AgentToolEndpoints.ReadChatTranscriptToolName);

    [Fact(DisplayName =
        "read_chat_transcript and the chat.read-transcript-request synapse answer from the same durable transcript")]
    public async Task McpToolAndSynapseHandlerShareOneReadPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string chatName = "shared-read-path";

        test.Chat().Reply("Noted: north star");
        await test.Client.GetGrainProxy<IChat>(chatName).Send(new SendMessage(CommandId.New(), "north star"));

        var tools = new DigitalBrainIntrospectionTools(test.Client);
        var page = await tools.ReadChatTranscriptAsync(chatName);

        var read = await test.Client.Get<IChat>()
            .SendAsync(new ReadTranscriptRequest(chatName), cancellationToken);

        Assert.Equal(chatName, page.Chat);
        Assert.Equal(read.Transcript.Turns.Count, page.Turns.Count);
        Assert.Collection(
            page.Turns,
            turn => Assert.Equal(("you", "north star"), (turn.Speaker, turn.Text)),
            turn => Assert.Equal(("brain", "Noted: north star"), (turn.Speaker, turn.Text)));
        Assert.Collection(
            read.Transcript.Turns,
            turn => Assert.Equal(new ChatTurn(FromUser: true, "north star"), turn),
            turn => Assert.Equal(new ChatTurn(FromUser: false, "Noted: north star"), turn));
    }
}
