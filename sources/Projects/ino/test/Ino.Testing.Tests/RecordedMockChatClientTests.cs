using Microsoft.Extensions.AI;
using Xunit;

namespace Ino.Testing.Tests;

public sealed class RecordedMockChatClientTests
{
    [Fact]
    public async Task Match_HelloWorld_ReturnsRecordedText()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromFile("fixtures/sample.llm.recordings.yml");

        var response = await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello world") },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("hi there!", response.Messages[0].Text);
    }

    [Fact]
    public async Task Match_AirportCode_ReturnsRecordedText()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromFile("fixtures/sample.llm.recordings.yml");

        var response = await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "resolve airport code for Tokyo") },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("NRT", response.Messages[0].Text);
    }

    [Fact]
    public async Task Miss_ThrowsMockLlmMissException_WithUnmatchedPrompt()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromFile("fixtures/sample.llm.recordings.yml");

        var act = () => client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "completely unmatched prompt") },
            cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<MockLlmMissException>(act);
        Assert.Contains("completely unmatched prompt", ex.UnmatchedPrompt);
    }

    [Fact]
    public async Task Miss_AccumulatesInUnmatchedPromptsList()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromFile("fixtures/sample.llm.recordings.yml");
        var ct = TestContext.Current.CancellationToken;

        try
        {
            await client.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, "miss one") },
                cancellationToken: ct);
        }
        catch (MockLlmMissException) { }

        try
        {
            await client.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, "miss two") },
                cancellationToken: ct);
        }
        catch (MockLlmMissException) { }

        Assert.Equal(2, client.UnmatchedPrompts.Count);
        Assert.Contains(client.UnmatchedPrompts, p => p.Contains("miss one"));
        Assert.Contains(client.UnmatchedPrompts, p => p.Contains("miss two"));
    }

    [Fact]
    public async Task InlineYaml_LoadsCorrectly()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromYaml("""
            - match: "inline"
              text: "worked"
            """);

        var response = await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "inline test") },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("worked", response.Messages[0].Text);
    }
}
