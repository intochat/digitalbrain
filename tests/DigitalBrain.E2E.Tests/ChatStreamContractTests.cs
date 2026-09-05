using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.Kernel;
using DigitalBrain.Product.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.E2E.Tests;

public sealed class ChatStreamContractTests
{
    [Fact]
    public async Task Mixed_stream_preserves_text_content_and_reports_terminal_failure()
    {
        var command = CommandId.New();
        var turn = TurnId.New();
        var failure = ChatTurnStream.ForTerminal(new TurnLifecycle(
            turn, command, new NeuronId("chat", new OwnerId("dev"), "main"), ChatTurnStatus.Failed,
            "internal storage exception with secret transport details"));
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await SseResponse.WriteAsync(context.Response, Events(failure), TestContext.Current.CancellationToken);

        var wire = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("event: chat-delta", wire, StringComparison.Ordinal);
        Assert.Contains("event: chat-accepted", wire, StringComparison.Ordinal);
        var acceptedLine = wire.Split('\n').First(line => line.StartsWith("data: ", StringComparison.Ordinal));
        using var accepted = JsonDocument.Parse(acceptedLine[6..]);
        Assert.Equal(command.ToString(), accepted.RootElement.GetProperty("commandId").GetString());
        Assert.Equal(turn.ToString(), accepted.RootElement.GetProperty("turnId").GetString());
        Assert.Contains("\"$type\":\"text\"", wire, StringComparison.Ordinal);
        Assert.Contains("event: chat-error", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("secret transport", wire, StringComparison.Ordinal);
        var errorLine = wire.Split('\n').Last(line => line.StartsWith("data: ", StringComparison.Ordinal));
        using var error = JsonDocument.Parse(errorLine[6..]);
        Assert.Equal("Failed", error.RootElement.GetProperty("status").GetString());
        Assert.Equal(command.ToString(), error.RootElement.GetProperty("commandId").GetString());
        Assert.Equal(turn.ToString(), error.RootElement.GetProperty("turnId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.RootElement.GetProperty("message").GetString()));
    }

    private static async IAsyncEnumerable<SseItem<object>> Events(ChatStreamError failure)
    {
        yield return new(new ChatStreamAccepted(failure.CommandId, failure.TurnId), HttpSurfacePaths.ChatAcceptedEvent);
        yield return new(new ChatResponseUpdate(ChatRole.Assistant, "hello"), HttpSurfacePaths.ChatDeltaEvent);
        await Task.CompletedTask;
        yield return new(failure, HttpSurfacePaths.ChatErrorEvent);
    }
}
