using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Sdk;

public sealed class McpHttpToolTests
{
    private static readonly Identity Alice = new(new OwnerId("http-test"), "gmail", "alice");

    [Fact]
    public async Task Http_discovery_preserves_native_schemas_and_explicit_admission()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        await using var client = Create(server, credentials, ["read"]);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        Assert.True(JsonElement.DeepEquals(HttpMcpServer.Definition("read").InputSchema, function.JsonSchema));
        var result = Assert.IsType<JsonElement>(await function.InvokeAsync(new() { ["query"] = "native" }, TestContext.Current.CancellationToken));
        Assert.Equal("native", result.GetProperty("structuredContent").GetProperty("query").GetString());
        Assert.Equal(1, server.ToolCalls);
        Assert.Equal(0, credentials.Refreshes);
    }

    [Fact]
    public async Task Changed_connection_revision_rejects_prepared_function_before_network()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        await using var client = Create(server, credentials);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        credentials.Current = credentials.Current with { Revision = 2 };
        var error = await Assert.ThrowsAsync<McpOperationException>(() => function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(McpFailureKind.ConnectionChanged, error.Kind);
        Assert.Equal(0, server.ToolCalls);
        var fresh = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        _ = await fresh.InvokeAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, server.Connections);
    }

    [Fact]
    public async Task Principal_binding_is_checked_during_preparation_and_again_at_invocation()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        var activePrincipal = "alice";
        await using var client = Create(server, credentials, authorize: (identity, binding) =>
        {
            if (identity.Principal != binding.Principal || identity.Principal != activePrincipal)
            {
                throw new McpOperationException("The current actor cannot use this connection.", McpFailureKind.AccessDenied);
            }
        });
        await Assert.ThrowsAsync<McpOperationException>(() => client.GetToolsAsync(Alice with { Principal = "bob" }, TestContext.Current.CancellationToken));
        Assert.Equal(0, server.Connections);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        activePrincipal = "bob";
        var error = await Assert.ThrowsAsync<McpOperationException>(() => function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(McpFailureKind.AccessDenied, error.Kind);
        Assert.Equal(0, server.ToolCalls);
    }

    [Fact]
    public async Task Specialist_identities_do_not_share_a_session_with_the_same_owner_and_principal()
    {
        await using var server = await HttpMcpServer.StartAsync();
        await using var client = Create(server, new Credentials());
        _ = await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken);
        _ = await client.GetToolsAsync(Alice with { Specialist = "salesforce" }, TestContext.Current.CancellationToken);
        _ = await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken);
        Assert.Equal(2, server.Connections);
    }

    [Fact]
    public async Task Known_read_rejected_once_refreshes_once_and_replays_once()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        await using var client = Create(server, credentials);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        server.Rejections = 1;
        _ = await function.InvokeAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, server.ToolCalls);
        Assert.Equal(1, credentials.Refreshes);
        Assert.Equal(0, credentials.Rejects);
        Assert.Equal(2, server.Connections);
    }

    [Fact]
    public async Task Repeated_unauthorized_read_requires_authentication_after_one_refresh()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        await using var client = Create(server, credentials);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        server.Rejections = 5;
        await Assert.ThrowsAsync<McpAuthenticationRequiredException>(() => function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(2, server.ToolCalls);
        Assert.Equal(1, credentials.Refreshes);
        Assert.Equal(1, credentials.Rejects);
    }

    [Fact]
    public async Task Writes_are_never_replayed_even_when_server_annotations_claim_readonly()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        await using var client = Create(server, credentials, ["write"]);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        server.Rejections = 1;
        await Assert.ThrowsAsync<McpAuthenticationRequiredException>(() => function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, server.ToolCalls);
        Assert.Equal(0, credentials.Refreshes);
        Assert.Equal(1, credentials.Rejects);
    }

    [Fact]
    public async Task Uncertain_http_failure_is_sanitized_without_refresh_or_replay()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        await using var client = Create(server, credentials);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        server.FailCalls = true;
        var error = await Assert.ThrowsAsync<McpOperationException>(() => function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(McpFailureKind.Unavailable, error.Kind);
        Assert.DoesNotContain("private-response-body", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, server.ToolCalls);
        Assert.Equal(0, credentials.Refreshes);
    }

    [Fact]
    public async Task Revision_change_while_a_reply_is_in_flight_does_not_return_evidence_from_old_account()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        await using var client = Create(server, credentials);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        server.HoldCalls = true;
        var pending = function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask();
        await server.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        credentials.Current = credentials.Current with { Revision = 2 };
        server.ReleaseCall.TrySetResult();
        var error = await Assert.ThrowsAsync<McpOperationException>(() => pending);
        Assert.Equal(McpFailureKind.ConnectionChanged, error.Kind);
        Assert.Equal(1, server.ToolCalls);
    }

    [Fact]
    public async Task Http_response_budget_is_enforced_before_unbounded_protocol_content_is_returned()
    {
        await using var server = await HttpMcpServer.StartAsync();
        await using var client = Create(server, new Credentials(), options: new() { ResponseBudgetBytes = 4096 });
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        server.ResultText = new string('x', 10000);
        await Assert.ThrowsAsync<McpOperationException>(() => function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, server.ToolCalls);
    }

    [Fact]
    public async Task Missing_credentials_never_start_an_http_connection()
    {
        await using var server = await HttpMcpServer.StartAsync();
        await using var client = Create(server, new Credentials { Missing = true });
        await Assert.ThrowsAsync<McpAuthenticationRequiredException>(() => client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task Refresh_cannot_replay_a_read_against_a_replaced_connection()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        credentials.OnRefresh = () => credentials.Current = credentials.Current with { Revision = 2 };
        await using var client = Create(server, credentials);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        server.Rejections = 1;
        var error = await Assert.ThrowsAsync<McpOperationException>(() => function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(McpFailureKind.ConnectionChanged, error.Kind);
        Assert.Equal(1, server.ToolCalls);
        Assert.Equal(1, credentials.Refreshes);
    }

    [Fact]
    public async Task Http_cancellation_does_not_replay_and_next_call_reconnects()
    {
        await using var server = await HttpMcpServer.StartAsync();
        var credentials = new Credentials();
        await using var client = Create(server, credentials);
        var function = Assert.Single(await client.GetToolsAsync(Alice, TestContext.Current.CancellationToken));
        server.HoldCalls = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = function.InvokeAsync(new(), cancellation.Token).AsTask();
        await server.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, server.ToolCalls);
        Assert.Equal(0, credentials.Refreshes);
        server.HoldCalls = false;
        server.ReleaseCall.TrySetResult();
        _ = await function.InvokeAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, server.Connections);
        Assert.Equal(2, server.ToolCalls);
    }

    private static McpDiscoveredToolClient<Identity> Create(HttpMcpServer server, Credentials credentials,
        string[]? allowed = null, Action<Identity, Binding>? authorize = null, McpSessionOptions? options = null)
        => McpDiscoveredToolClient<Identity>.ForHttp(server.Endpoint, credentials, static identity => identity.Owner,
            authorize ?? ((identity, binding) =>
            {
                if (identity.Principal != binding.Principal)
                {
                    throw new McpOperationException("The current actor cannot use this connection.", McpFailureKind.AccessDenied);
                }
            }), allowed ?? ["read"], new McpToolPolicy(static name => name == "read"), options ?? new() { Timeout = TimeSpan.FromSeconds(3) });

    private sealed record Identity(OwnerId Owner, string Specialist, string Principal);
    private sealed record Binding(string Account, int Revision, string Principal);

    private sealed class Credentials : IMcpCredentials<Binding>
    {
        internal Binding Current = new("selected-account", 1, "alice");
        internal int Refreshes;
        internal int Rejects;
        internal bool Missing;
        internal Action? OnRefresh;
        public Binding Connection(OwnerId owner) => Missing ? throw new McpAuthenticationRequiredException() : Current;
        public Task<string> AccessTokenAsync(OwnerId owner, Binding connection, bool refresh, CancellationToken cancellationToken)
        {
            if (refresh)
            {
                Interlocked.Increment(ref Refreshes);
                OnRefresh?.Invoke();
            }
            return Task.FromResult("fixture-token");
        }
        public Task RejectAsync(OwnerId owner, Binding connection, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Rejects);
            return Task.CompletedTask;
        }
    }

    private sealed class HttpMcpServer(WebApplication app) : IAsyncDisposable
    {
        internal McpEndpoint Endpoint { get; private set; } = null!;
        internal int Connections;
        internal int ToolCalls;
        internal int Rejections;
        internal bool FailCalls;
        internal bool HoldCalls;
        internal string ResultText = "Native provider content";
        internal readonly TaskCompletionSource CallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static async Task<HttpMcpServer> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var application = builder.Build();
            var server = new HttpMcpServer(application);
            application.MapPost("/mcp", server.HandleAsync);
            await application.StartAsync(TestContext.Current.CancellationToken);
            var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            server.Endpoint = new McpEndpoint("fixture-http", new Uri(address + "/mcp"));
            return server;
        }

        internal static Tool Definition(string name) => new()
        {
            Name = name,
            Description = "Native description",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { query = new { type = "string" } } }),
            Annotations = new ToolAnnotations { ReadOnlyHint = true },
        };

        private async Task HandleAsync(HttpContext context)
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var message = document.RootElement;
            if (!message.TryGetProperty("id", out var id))
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            object result;
            switch (message.GetProperty("method").GetString())
            {
                case "initialize":
                    Interlocked.Increment(ref Connections);
                    result = new { protocolVersion = "2025-06-18", capabilities = new { tools = new { } }, serverInfo = new { name = "fixture", version = "1" } };
                    break;
                case "tools/list":
                    result = new { tools = new[] { Definition("read"), Definition("write") } };
                    break;
                case "tools/call":
                    Interlocked.Increment(ref ToolCalls);
                    CallStarted.TrySetResult();
                    if (Interlocked.Decrement(ref Rejections) >= 0)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }
                    if (FailCalls)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsync("private-response-body", context.RequestAborted);
                        return;
                    }
                    if (HoldCalls)
                    {
                        await ReleaseCall.Task.WaitAsync(context.RequestAborted);
                    }
                    var arguments = message.GetProperty("params").GetProperty("arguments");
                    result = new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = ResultText }],
                        StructuredContent = arguments.Clone(),
                    };
                    break;
                default:
                    // Older servers reject the SDK's server/discover probe with a protocol error,
                    // allowing the client to negotiate the initialize handshake automatically.
                    await context.Response.WriteAsJsonAsync(new
                    {
                        jsonrpc = "2.0", id,
                        error = new { code = -32601, message = "Method not found" },
                    }, McpJsonUtilities.DefaultOptions, context.RequestAborted);
                    return;
            }
            await context.Response.WriteAsJsonAsync(new { jsonrpc = "2.0", id, result }, McpJsonUtilities.DefaultOptions, context.RequestAborted);
        }

        public async ValueTask DisposeAsync()
        {
            ReleaseCall.TrySetResult();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
