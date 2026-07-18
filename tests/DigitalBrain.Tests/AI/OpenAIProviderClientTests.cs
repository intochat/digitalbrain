using System.ClientModel;
using System.Net;
using System.Text.Json;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using OpenAI.Embeddings;
using Xunit;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace DigitalBrain.Tests.AI;

public sealed class OpenAIProviderClientTests
{
    [Theory]
    [InlineData(typeof(GptFast))]
    [InlineData(typeof(GptReasoning))]
    public async Task Chat_descriptors_create_the_official_client_with_endpoint_model_auth_and_response_mapping(
        Type descriptorType)
    {
        var descriptor = Assert.IsAssignableFrom<ChatModelDescriptor>(
            Activator.CreateInstance(descriptorType));
        var handler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.Json(ChatResponseJson(descriptor.ModelId, "answer"))));
        using var httpClient = new HttpClient(handler);
        var client = OpenAIProviderClientFactory.CreateChat(
            OpenAIOptions(),
            descriptor.ModelId,
            NullLoggerFactory.Instance,
            httpClient);

        var response = await client.GetResponseAsync(
            [new AIChatMessage(ChatRole.User, "hello")]);

        Assert.Equal(ModelProvider.OpenAI, descriptor.Provider);
        Assert.Equal("answer", response.Text);
        Assert.IsType<ChatClient>(client.GetService(typeof(ChatClient)));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://openai.test/v1/chat/completions", request.Uri.ToString());
        Assert.Equal("Bearer test-openai-key", Assert.Single(request.Headers["Authorization"]));
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(descriptor.ModelId, body.RootElement.GetProperty("model").GetString());
        Assert.Equal(
            "hello",
            body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Embedding_factory_uses_the_official_client_and_maps_vectors()
    {
        var descriptor = new TextEmbedding();
        const string json = """
            {
              "object": "list",
              "data": [
                {
                  "object": "embedding",
                  "index": 0,
                  "embedding": [0.25, -0.5, 0.75]
                }
              ],
              "model": "text-embedding-3-small",
              "usage": { "prompt_tokens": 2, "total_tokens": 2 }
            }
            """;
        var handler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.Json(json)));
        using var httpClient = new HttpClient(handler);
        var generator = OpenAIProviderClientFactory.CreateEmbedding(
            OpenAIOptions(),
            descriptor.ModelId,
            NullLoggerFactory.Instance,
            httpClient);

        var generated = await generator.GenerateAsync(["embed me"]);

        var embedding = Assert.Single(generated);
        Assert.Equal(ModelProvider.OpenAI, descriptor.Provider);
        Assert.Equal([0.25f, -0.5f, 0.75f], embedding.Vector.ToArray());
        Assert.IsType<EmbeddingClient>(generator.GetService(typeof(EmbeddingClient)));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://openai.test/v1/embeddings", request.Uri.ToString());
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(descriptor.ModelId, body.RootElement.GetProperty("model").GetString());
        Assert.Equal("embed me", body.RootElement.GetProperty("input")[0].GetString());
    }

    [Fact]
    public async Task Streaming_maps_incremental_official_provider_updates()
    {
        var stream = """
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1,"model":"gpt-5-mini","choices":[{"index":0,"delta":{"role":"assistant","content":"hel"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1,"model":"gpt-5-mini","choices":[{"index":0,"delta":{"content":"lo"},"finish_reason":"stop"}]}

            data: [DONE]

            """;
        var handler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.EventStream(stream)));
        using var httpClient = new HttpClient(handler);
        var client = OpenAIProviderClientFactory.CreateChat(
            OpenAIOptions(),
            "gpt-5-mini",
            NullLoggerFactory.Instance,
            httpClient);
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync(
                           [new AIChatMessage(ChatRole.User, "hello")]))
            updates.Add(update);

        Assert.Equal("hello", string.Concat(updates.Select(update => update.Text)));
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
        using var httpClient = new HttpClient(handler);
        var client = OpenAIProviderClientFactory.CreateChat(
            OpenAIOptions(),
            "gpt-5-mini",
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
                """{"error":{"message":"bad request","type":"invalid_request_error"}}""",
                HttpStatusCode.BadRequest)));
        using var httpClient = new HttpClient(handler);
        var client = OpenAIProviderClientFactory.CreateChat(
            OpenAIOptions(),
            "gpt-5-mini",
            NullLoggerFactory.Instance,
            httpClient);

        var failure = await Assert.ThrowsAsync<ClientResultException>(() =>
            client.GetResponseAsync([new AIChatMessage(ChatRole.User, "hello")]));

        Assert.Equal(400, failure.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Malformed_success_response_is_not_converted_to_canned_output()
    {
        var handler = new ProviderTestHttpHandler((_, _) => Task.FromResult(
            ProviderTestHttpHandler.Json("""{"choices":"not-an-array"}""")));
        using var httpClient = new HttpClient(handler);
        var client = OpenAIProviderClientFactory.CreateChat(
            OpenAIOptions(),
            "gpt-5-mini",
            NullLoggerFactory.Instance,
            httpClient);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.GetResponseAsync([new AIChatMessage(ChatRole.User, "hello")]));
    }

    private static OpenAIProviderOptions OpenAIOptions() =>
        new()
        {
            ApiKey = "test-openai-key",
            Endpoint = new Uri("https://openai.test/v1"),
            FastModelId = new GptFast().ModelId,
            ReasoningModelId = new GptReasoning().ModelId,
            EmbeddingModelId = new TextEmbedding().ModelId
        };

    internal static string ChatResponseJson(string model, string text) =>
        $$"""
          {
            "id": "chatcmpl-test",
            "object": "chat.completion",
            "created": 1,
            "model": "{{model}}",
            "choices": [
              {
                "index": 0,
                "message": { "role": "assistant", "content": "{{text}}" },
                "finish_reason": "stop"
              }
            ],
            "usage": {
              "prompt_tokens": 1,
              "completion_tokens": 1,
              "total_tokens": 2
            }
          }
          """;
}
