using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class McpToolFacts
{
    [Fact]
    public void TheMcpAllowlistRegistersExactlyTheListedServers()
    {
        var services = new ServiceCollection();
        AIModule.RegisterMcpBridge(services, "salesforce=https://mcp.example.test/tools;github=https://github.example.test/mcp");
        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(IAgentToolSource)));
    }

    [Fact]
    public void AnEmptyMcpAllowlistRegistersNoServer()
    {
        var services = new ServiceCollection();
        AIModule.RegisterMcpBridge(services, null);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAgentToolSource));
    }

    [Fact]
    public void AMalformedMcpAllowlistEntryIsRejected()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => AIModule.RegisterMcpBridge(services, "salesforce"));
    }

    [Fact]
    public async Task RunnerDisposesMcpSessionAndDoesNotCompleteAfterProviderFailure()
    {
        using var handler = new ProtocolHandler { FailCall = true };
        using var services = new ServiceCollection()
            .AddSingleton<IChatClient>(new ToolCallingClient())
            .AddMcpAgentTools("mail", _ => Transport(handler)).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(
            new("agent", "run", "scope", [], "lookup", null, ToolNames: ["mcp_mail_lookup"]), TestContext.Current.CancellationToken))
        { events.Add(item); }
        Assert.Single(events.OfType<AgentTurnEvent.ToolStarted>());
        Assert.Single(events.OfType<AgentTurnEvent.ToolFailed>());
        Assert.Single(events.OfType<AgentTurnEvent.Failed>());
        Assert.Empty(events.OfType<AgentTurnEvent.Finished>());
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task SelectedToolsRetainSchemaAndRouteToOriginalName()
    {
        using var handler = new ProtocolHandler();
        using var services = Services(handler);
        var source = services.GetRequiredService<IAgentToolSource>();
        await using var session = await source.OpenAsync(["mcp_mail_lookup"], Context, TestContext.Current.CancellationToken);
        var tool = Assert.Single(session.Tools);
        Assert.Equal("mcp_mail_lookup", tool.Name);
        Assert.Equal("string", tool.JsonSchema.GetProperty("properties").GetProperty("query").GetProperty("type").GetString());
        var result = await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "hello" }, TestContext.Current.CancellationToken);
        Assert.Contains("hello", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        Assert.Equal("lookup", handler.CalledTool);
        await session.DisposeAsync();
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task UnselectedServerNeverConnects()
    {
        using var handler = new ProtocolHandler();
        using var services = Services(handler);
        await using var session = await services.GetRequiredService<IAgentToolSource>().OpenAsync(["native"], Context, TestContext.Current.CancellationToken);
        Assert.Empty(session.Tools);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task ProviderErrorIsFailure()
    {
        using var handler = new ProtocolHandler { FailCall = true };
        using var services = Services(handler);
        await using var session = await services.GetRequiredService<IAgentToolSource>().OpenAsync(["mcp_mail_lookup"], Context, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Assert.Single(session.Tools).InvokeAsync(new(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingSelectedToolFailsAndDisposesConnection()
    {
        using var handler = new ProtocolHandler();
        using var services = Services(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<IAgentToolSource>().OpenAsync(["mcp_mail_missing"], Context, TestContext.Current.CancellationToken));
        Assert.True(handler.Disposed);
    }

    private static AgentToolContext Context() => new("scope", "run", "call");
    private static ServiceProvider Services(ProtocolHandler handler) => new ServiceCollection()
        .AddMcpAgentTools("mail", _ => Transport(handler)).BuildServiceProvider();
    private static HttpClientTransport Transport(ProtocolHandler handler) => new(
            new() { Endpoint = new("https://mcp.invalid"), TransportMode = HttpTransportMode.StreamableHttp },
            new HttpClient(handler), ownsHttpClient: true);

    private sealed class ToolCallingClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Assert.Equal("mcp_mail_lookup", Assert.Single(options!.Tools!).Name);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call", "mcp_mail_lookup", new Dictionary<string, object?> { ["query"] = "hello" })])));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }

    private sealed class ProtocolHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool FailCall { get; init; }
        public bool Disposed { get; private set; }
        public string? CalledTool { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method != HttpMethod.Post) { return new(HttpStatusCode.MethodNotAllowed); }
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            if (!root.TryGetProperty("id", out var id)) { return new(HttpStatusCode.Accepted); }
            var method = root.GetProperty("method").GetString();
            if (method == "server/discover")
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    { jsonrpc = "2.0", id, error = new { code = -32601, message = "Method not found" } }), Encoding.UTF8, "application/json")
                };
            }
            object result;
            if (method == "initialize")
            { result = new { protocolVersion = "2025-11-25", capabilities = new { tools = new { } }, serverInfo = new { name = "test", version = "1" } }; }
            else if (method == "tools/list")
            { result = new { tools = new[] { new { name = "lookup", description = "Lookup a message", inputSchema = new { type = "object", properties = new { query = new { type = "string" } } } } } }; }
            else
            {
                CalledTool = root.GetProperty("params").GetProperty("name").GetString();
                result = new { content = new[] { new { type = "text", text = "hello" } }, isError = FailCall };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
