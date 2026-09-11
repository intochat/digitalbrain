using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.AI.WebSearch;
using DigitalBrain.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ConversationalAgentFacts
{
    [Fact]
    public async Task Search_tool_results_are_streamed_and_returned_to_the_model()
    {
        using var model = new ScriptedChatClient();
        model.CallTool("search_web", "{\"query\":\"Microsoft Agent Framework\",\"maxResults\":3}");
        model.Say("Microsoft Agent Framework supports streaming. [Source](https://learn.microsoft.com/agent-framework/)");
        var search = new SearchFixture();
        await using var app = await StartAsync(model, search);
        using var client = app.GetTestClient();

        var events = await RunAsync(client, "Find current Microsoft agent docs", Guid.NewGuid().ToString());

        Assert.Contains(events, item => Type(item) == "TEXT_MESSAGE_CONTENT" && item.GetProperty("delta").GetString()!.Contains("supports streaming", StringComparison.Ordinal));
        Assert.Contains(events, item => Type(item) == "TOOL_CALL_START" && item.GetProperty("toolCallName").GetString() == "search_web");
        Assert.Contains(events, item => Type(item) == "TOOL_CALL_RESULT" && item.ToString().Contains("learn.microsoft.com", StringComparison.Ordinal));
        using var result = JsonDocument.Parse(events.Single(item => Type(item) == "TOOL_CALL_RESULT").GetProperty("content").GetString()!);
        Assert.Equal("https://learn.microsoft.com/agent-framework/", result.RootElement.GetProperty("results")[0].GetProperty("url").GetString());
        Assert.Equal("RUN_FINISHED", Type(events[^1]));
        Assert.Equal("Microsoft Agent Framework", search.Query);
        Assert.Equal(3, search.MaxResults);
        Assert.Contains(model.Calls.SelectMany(call => call).SelectMany(message => message.Contents), content => content is FunctionResultContent);
    }

    [Fact]
    public async Task Conversation_keeps_history_and_separates_new_sessions()
    {
        using var model = new ScriptedChatClient();
        model.Say("Hello Ada");
        model.Say("Your name is Ada");
        model.Say("I do not know your name");
        await using var app = await StartAsync(model, new SearchFixture());
        using var client = app.GetTestClient();

        var first = await RunAsync(client, "My name is Ada", Guid.NewGuid().ToString());
        var started = first.Single(item => Type(item) == "RUN_STARTED");
        await RunAsync(client, "What is my name?", started.GetProperty("threadId").GetString()!, started.GetProperty("runId").GetString());
        await RunAsync(client, "What is my name?", Guid.NewGuid().ToString());

        Assert.Contains(model.Calls[1], message => message.Text.Contains("My name is Ada", StringComparison.Ordinal));
        Assert.Contains(model.Calls[1], message => message.Text.Contains("Hello Ada", StringComparison.Ordinal));
        Assert.DoesNotContain(model.Calls[2], message => message.Text.Contains("Ada", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Text_reaches_the_client_before_completion_and_disconnect_cancels_the_model()
    {
        using var model = new WaitingStreamingClient();
        await using var app = await StartAsync(model, new SearchFixture());
        using var client = app.GetTestClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/agent")
        {
            Content = JsonContent.Create(Input("Stream a response", Guid.NewGuid().ToString())),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellation.Token) is { } line)
        {
            if (!line.Contains("TEXT_MESSAGE_CONTENT", StringComparison.Ordinal))
            {
                continue;
            }
            Assert.Contains("First fragment", line, StringComparison.Ordinal);
            Assert.False(model.Cancelled.Task.IsCompleted);
            cancellation.Cancel();
            // ResponseHeadersRead has finished SendAsync; closing the response is the disconnect.
            response.Dispose();
            await model.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            return;
        }
        Assert.Fail("The endpoint ended without streaming text.");
    }

    [Fact]
    public async Task Agent_endpoint_uses_the_existing_authentication_gate()
    {
        using var model = new ScriptedChatClient();
        await using var app = await StartAsync(model, new SearchFixture(), gated: true);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync("/agent", Input("Hello", Guid.NewGuid().ToString()), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public async Task Provider_failure_produces_a_terminal_error_event()
    {
        using var model = new ScriptedChatClient();
        model.TimeOut(nameof(TimeoutException));
        await using var app = await StartAsync(model, new SearchFixture());
        using var client = app.GetTestClient();
        var events = await RunAsync(client, "Hello", Guid.NewGuid().ToString());
        Assert.Equal("RUN_ERROR", Type(events[^1]));
        Assert.DoesNotContain(events, item => Type(item) == "RUN_FINISHED");
    }

    private static async Task<WebApplication> StartAsync(IChatClient model, IWebSearch search, bool gated = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (gated)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BasicAuthGate.UsernameConfigurationKey] = "owner",
                [BasicAuthGate.PasswordConfigurationKey] = "test-password",
            });
        }
        // Production AIClients also supplies a function-invoking pipeline.
        builder.Services.AddSingleton(new ChatClientBuilder(model).UseFunctionInvocation().Build());
        builder.Services.AddSingleton(search);
        builder.AddConversationalAgent();
        var app = builder.Build();
        app.UseBasicAuthGate();
        app.MapConversationalAgent();
        await app.StartAsync();
        return app;
    }

    private static async Task<List<JsonElement>> RunAsync(HttpClient client, string text, string thread, string? parent = null)
    {
        using var response = await client.PostAsJsonAsync("/agent", Input(text, thread, parent));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return body.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[5..])).ToList();
    }

    private static string? Type(JsonElement item) => item.GetProperty("type").GetString();

    private static object Input(string text, string thread, string? parent = null) => new
    {
        threadId = thread,
        runId = Guid.NewGuid().ToString(),
        parentRunId = parent,
        messages = new[] { new { id = Guid.NewGuid().ToString(), role = "user", content = text } },
        tools = Array.Empty<object>(),
        context = Array.Empty<object>(),
        state = new { },
        forwardedProps = new { },
    };

    private sealed class WaitingStreamingClient : IChatClient
    {
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "First fragment") { MessageId = "response" };
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                Cancelled.TrySetResult();
            }
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("The agent must use streaming.");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class SearchFixture : IWebSearch
    {
        public string? Query { get; private set; }
        public int MaxResults { get; private set; }

        public Task<WebSearchResponse> SearchAsync(string query, int maxResults = 5, CancellationToken cancellationToken = default)
        {
            Query = query;
            MaxResults = maxResults;
            return Task.FromResult(new WebSearchResponse(null,
                [new WebSearchResult("Agent Framework", new Uri("https://learn.microsoft.com/agent-framework/"), "Microsoft agent documentation", 1)]));
        }
    }
}
