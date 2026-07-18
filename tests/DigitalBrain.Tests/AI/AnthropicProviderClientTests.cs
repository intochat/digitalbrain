using System.Net;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace DigitalBrain.Tests.AI;

public sealed class AnthropicProviderClientTests
{
    [Fact]
    public async Task Chat_factory_uses_the_official_client_endpoint_model_auth_and_response_mapping()
    {
        var descriptor = new ClaudeBalanced();
        var handler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.Json(MessageResponseJson("balanced answer"))));
        using var httpClient = CreateHttpClient(handler);
        var client = AnthropicProviderClientFactory.CreateChat(
            AnthropicOptions(),
            descriptor.ModelId,
            NullLoggerFactory.Instance,
            httpClient);

        var response = await client.GetResponseAsync(
            [new AIChatMessage(ChatRole.User, "hello")]);

        Assert.Equal(ModelProvider.Anthropic, descriptor.Provider);
        Assert.Equal("balanced answer", response.Text);
        Assert.NotNull(client.GetService(typeof(IAnthropicClient)));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://anthropic.test/v1/messages", request.Uri.ToString());
        Assert.Equal("test-anthropic-key", Assert.Single(request.Headers["X-Api-Key"]));
        Assert.Equal("2023-06-01", Assert.Single(request.Headers["anthropic-version"]));
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(descriptor.ModelId, body.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            "hello",
            body.RootElement
                .GetProperty("messages")[0]
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString());
    }

    [Fact]
    public async Task Streaming_maps_incremental_official_provider_updates()
    {
        var stream = """
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_test","type":"message","role":"assistant","model":"claude-sonnet-4-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":0}}}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"bal"}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"anced"}}

            event: content_block_stop
            data: {"type":"content_block_stop","index":0}

            event: message_delta
            data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":1}}

            event: message_stop
            data: {"type":"message_stop"}

            """;
        var handler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.EventStream(stream)));
        using var httpClient = CreateHttpClient(handler);
        var client = AnthropicProviderClientFactory.CreateChat(
            AnthropicOptions(),
            "claude-sonnet-4-5",
            NullLoggerFactory.Instance,
            httpClient);
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync(
                           [new AIChatMessage(ChatRole.User, "hello")]))
            updates.Add(update);

        Assert.Equal("balanced", string.Concat(updates.Select(update => update.Text)));
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task Cancellation_reaches_the_official_HTTP_transport()
    {
        var observedCancellation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ProviderTestHttpHandler(async (_, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(() => observedCancellation.SetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        });
        using var httpClient = CreateHttpClient(handler);
        var client = AnthropicProviderClientFactory.CreateChat(
            AnthropicOptions(),
            "claude-sonnet-4-5",
            NullLoggerFactory.Instance,
            httpClient);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetResponseAsync(
                [new AIChatMessage(ChatRole.User, "hello")],
                cancellationToken: cancellation.Token));

        await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Non_success_status_preserves_the_official_provider_error()
    {
        var handler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.Json(
                """
                {
                  "type": "error",
                  "error": {
                    "type": "authentication_error",
                    "message": "invalid key"
                  }
                }
                """,
                HttpStatusCode.Unauthorized)));
        using var httpClient = CreateHttpClient(handler);
        var client = AnthropicProviderClientFactory.CreateChat(
            AnthropicOptions(),
            "claude-sonnet-4-5",
            NullLoggerFactory.Instance,
            httpClient);

        var failure = await Assert.ThrowsAsync<AnthropicUnauthorizedException>(() =>
            client.GetResponseAsync([new AIChatMessage(ChatRole.User, "hello")]));

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Malformed_success_response_preserves_the_official_invalid_data_error()
    {
        var handler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.Json("""{"type":"message"}""")));
        using var httpClient = CreateHttpClient(handler);
        var client = AnthropicProviderClientFactory.CreateChat(
            AnthropicOptions(),
            "claude-sonnet-4-5",
            NullLoggerFactory.Instance,
            httpClient);

        await Assert.ThrowsAsync<AnthropicInvalidDataException>(() =>
            client.GetResponseAsync([new AIChatMessage(ChatRole.User, "hello")]));
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

    private static AnthropicProviderOptions AnthropicOptions() =>
        new()
        {
            ApiKey = "test-anthropic-key",
            Endpoint = new Uri("https://anthropic.test"),
            BalancedModelId = new ClaudeBalanced().ModelId
        };

    internal static string MessageResponseJson(string text) =>
        $$"""
          {
            "id": "msg_test",
            "type": "message",
            "role": "assistant",
            "model": "claude-sonnet-4-5",
            "content": [
              { "type": "text", "text": "{{text}}" }
            ],
            "stop_reason": "end_turn",
            "stop_sequence": null,
            "usage": {
              "input_tokens": 1,
              "output_tokens": 1
            }
          }
          """;
}
