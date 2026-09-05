using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Sdk;

public sealed class McpDiscoveredToolTests
{
    [Fact]
    public async Task Discovery_preserves_schemas_metadata_and_native_result_envelope()
    {
        await using var server = new FakeMcpServer();
        await using var client = Create(server);
        var function = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));

        Assert.Equal("Read observations", function.GetService<McpDiscoveredTool>()!.Title);
        Assert.Equal("object", function.JsonSchema.GetProperty("type").GetString());
        Assert.True(JsonElement.DeepEquals(server.Tools[0].InputSchema, function.JsonSchema));
        Assert.True(JsonElement.DeepEquals(server.Tools[0].OutputSchema!.Value, function.ReturnJsonSchema!.Value));
        Assert.Equal("server-owned", function.GetService<McpClientTool>()!.ProtocolTool.Meta!["origin"]!.GetValue<string>());
        Assert.Equal("fixture", function.GetService<McpDiscoveredTool>()!.ConnectionName);

        var result = Assert.IsType<JsonElement>(await function.InvokeAsync(new() { ["resource"] = "kernel" }, TestContext.Current.CancellationToken));
        Assert.Equal("kernel", result.GetProperty("structuredContent").GetProperty("resource").GetString());
        Assert.Equal("Native text evidence", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("unchanged", result.GetProperty("_meta").GetProperty("providerField").GetString());
        Assert.False(McpDiscoveredTool.IsError(result));
        Assert.False(McpDiscoveredTool.IsTruncated(result));
        Assert.Equal(1, server.Connections);
    }

    [Fact]
    public async Task Text_only_results_remain_text_without_a_provider_specific_parser()
    {
        await using var server = new FakeMcpServer { IncludeStructuredContent = false, Text = "The application is reachable." };
        await using var client = Create(server);
        var function = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        var result = Assert.IsType<JsonElement>(await function.InvokeAsync(new(), TestContext.Current.CancellationToken));
        Assert.False(result.TryGetProperty("structuredContent", out _));
        Assert.Equal("The application is reachable.", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Catalog_notification_discovers_new_permitted_tools_and_schema_without_new_contracts()
    {
        await using var server = new FakeMcpServer();
        await using var client = Create(server, Options() with { AllowedToolNames = ["read", "search"] });
        var old = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        server.Tools = [Definition("read", "query"), Definition("search", "anything"), Definition("delete", "anything")];
        await server.NotifyCatalogChangedAsync();

        IReadOnlyList<AIFunction> refreshed = [];
        await EventuallyAsync(async () =>
        {
            refreshed = await client.GetToolsAsync("alice", TestContext.Current.CancellationToken);
            return refreshed.Count == 2;
        });
        Assert.DoesNotContain(refreshed, tool => tool.Name == "delete");
        var read = Assert.Single(refreshed, tool => tool.Name == "read");
        Assert.True(read.JsonSchema.GetProperty("properties").TryGetProperty("query", out _));
        var error = await Assert.ThrowsAsync<McpOperationException>(() => old.InvokeAsync(new() { ["resource"] = "kernel" }, TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("changed its schema", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, server.ToolCalls);

        // Even this prepared function binds to the current connection after the stale call reset it.
        _ = await read.InvokeAsync(new() { ["query"] = "recent errors" }, TestContext.Current.CancellationToken);
        Assert.Equal(1, server.ToolCalls);
        Assert.Equal(2, server.Connections);
    }

    [Fact]
    public async Task Tool_errors_retain_content_and_are_not_transport_failures()
    {
        await using var server = new FakeMcpServer { ErrorResult = true };
        await using var client = Create(server);
        var function = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        var result = Assert.IsType<JsonElement>(await function.InvokeAsync(new(), TestContext.Current.CancellationToken));

        Assert.True(McpDiscoveredTool.IsError(result));
        Assert.Equal("Native text evidence", result.GetProperty("content")[0].GetProperty("text").GetString());
        _ = await function.InvokeAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(1, server.Connections);
        Assert.Equal(2, server.ToolCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Over_budget_results_disclose_omission_and_preserve_error_flag(bool isError)
    {
        await using var server = new FakeMcpServer { ErrorResult = isError, Text = new string('x', 4000) };
        await using var client = Create(server, Options() with { ResponseBudgetBytes = 512 });
        var function = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        var result = Assert.IsType<JsonElement>(await function.InvokeAsync(new(), TestContext.Current.CancellationToken));

        Assert.True(McpDiscoveredTool.IsTruncated(result));
        Assert.Equal(isError, McpDiscoveredTool.IsError(result));
        Assert.False(result.TryGetProperty("structuredContent", out _));
        Assert.Contains("omitted", result.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(result.GetRawText()) <= 512);
    }

    [Fact]
    public async Task Identities_have_separate_selected_target_and_cached_catalogs()
    {
        await using var server = new FakeMcpServer();
        await using var client = new McpDiscoveredToolClient<string>(Options(),
            async (mcp, identity, token) =>
            {
                await mcp.CallToolAsync("bind", new Dictionary<string, object?> { ["target"] = identity }, cancellationToken: token);
            }, server.ConnectAsync);
        var tools = await Task.WhenAll(client.GetToolsAsync("alice", TestContext.Current.CancellationToken), client.GetToolsAsync("bob", TestContext.Current.CancellationToken));
        var alice = Assert.IsType<JsonElement>(await tools[0][0].InvokeAsync(new(), TestContext.Current.CancellationToken));
        var bob = Assert.IsType<JsonElement>(await tools[1][0].InvokeAsync(new(), TestContext.Current.CancellationToken));
        Assert.Equal("alice", alice.GetProperty("structuredContent").GetProperty("target").GetString());
        Assert.Equal("bob", bob.GetProperty("structuredContent").GetProperty("target").GetString());
        _ = await client.GetToolsAsync("alice", TestContext.Current.CancellationToken);
        Assert.Equal(2, server.Connections);
        Assert.Equal(2, server.CatalogReads);
        Assert.Equal(2, server.BindCalls);
    }

    [Fact]
    public async Task Failed_target_binding_does_not_expose_tools_and_reconnects_before_retrying()
    {
        await using var server = new FakeMcpServer();
        var failBinding = true;
        await using var client = new McpDiscoveredToolClient<string>(Options(),
            (_, _, _) => failBinding ? throw new InvalidOperationException("private target details") : Task.CompletedTask,
            server.ConnectAsync);
        var error = await Assert.ThrowsAsync<McpOperationException>(() => client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("private target details", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, server.CatalogReads);
        failBinding = false;
        Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        Assert.Equal(2, server.Connections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_or_deadline_does_not_replay_and_next_operation_reconnects(bool cancelCaller)
    {
        await using var server = new FakeMcpServer();
        await using var client = Create(server, Options() with { OperationTimeout = TimeSpan.FromMilliseconds(300) });
        var function = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        server.HoldCalls = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var call = function.InvokeAsync(new(), cancellation.Token).AsTask();
        await server.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        if (cancelCaller)
        {
            await cancellation.CancelAsync();
        }

        if (cancelCaller)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }
        else
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(() => call);
            Assert.Contains("not confirmed", error.Message, StringComparison.Ordinal);
        }
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.Equal(1, server.ToolCalls);
        server.HoldCalls = false;
        _ = await function.InvokeAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, server.ToolCalls);
        Assert.Equal(2, server.Connections);
    }

    [Fact]
    public async Task Disconnect_is_reported_without_replay_and_reconnects_on_next_call()
    {
        await using var server = new FakeMcpServer();
        await using var client = Create(server);
        var function = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        server.DisconnectCalls = true;
        var error = await Assert.ThrowsAsync<McpOperationException>(() => function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("not replayed", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, server.ToolCalls);
        server.DisconnectCalls = false;
        _ = await function.InvokeAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, server.ToolCalls);
        Assert.Equal(2, server.Connections);
    }

    [Fact]
    public async Task Disposal_cancels_active_calls_and_rejects_new_leases()
    {
        await using var server = new FakeMcpServer();
        await using var client = Create(server);
        var function = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        server.HoldCalls = true;
        var call = function.InvokeAsync(new(), TestContext.Current.CancellationToken).AsTask();
        await server.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await client.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        Assert.Equal(1, server.ToolCalls);
    }

    [Fact]
    public async Task Idle_pruning_releases_capacity_and_old_function_leases_new_session()
    {
        await using var server = new FakeMcpServer();
        await using var client = Create(server, Options() with { Capacity = 1, IdleTimeout = TimeSpan.FromMilliseconds(120) });
        var alice = Assert.Single(await client.GetToolsAsync("alice", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpOperationException>(() => client.GetToolsAsync("bob", TestContext.Current.CancellationToken));
        await EventuallyAsync(async () =>
        {
            try { _ = await client.GetToolsAsync("bob", TestContext.Current.CancellationToken); return true; }
            catch (McpOperationException) { return false; }
        });
        Assert.Equal(2, server.Connections);
        await client.InvalidateAsync("bob", TestContext.Current.CancellationToken);
        // Expiration removes the identity slot as well as stopping its old session.
        await EventuallyAsync(async () =>
        {
            try { _ = await alice.InvokeAsync(new(), TestContext.Current.CancellationToken); return true; }
            catch (McpOperationException) { return false; }
        });
        Assert.Equal(3, server.Connections);
    }

    private static McpStdioConnection Options() => new()
    {
        Name = "fixture", Command = "unused-test-transport", AllowedToolNames = ["read"],
        OperationTimeout = TimeSpan.FromSeconds(3),
    };

    private static McpDiscoveredToolClient<string> Create(FakeMcpServer server, McpStdioConnection? options = null)
        => new(options ?? Options(), null, server.ConnectAsync);

    private static Tool Definition(string name, string property) => new()
    {
        Name = name,
        Title = "Read observations",
        Description = "A description published by the server.",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object", properties = new Dictionary<string, object> { [property] = new { type = "string", minLength = 1 } }, required = new[] { property },
        }),
        OutputSchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { resource = new { type = "string" } } }),
        Meta = new JsonObject { ["origin"] = "server-owned" },
    };

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!await condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    // A small protocol server over real SDK stream transports. Its tool catalog and result schemas
    // are arbitrary JSON, so the tests catch accidental provider-specific mapping or stale sessions.
    internal sealed class FakeMcpServer : IAsyncDisposable
    {
        private readonly List<Connection> _connections = [];
        internal Tool[] Tools = [Definition("read", "resource")];
        internal int Connections;
        internal int CatalogReads;
        internal int ToolCalls;
        internal int BindCalls;
        internal bool ErrorResult;
        internal bool HoldCalls;
        internal bool DisconnectCalls;
        internal bool IncludeStructuredContent = true;
        internal string Text = "Native text evidence";
        internal Func<JsonElement, CallToolResult>? OnToolCall;
        internal readonly TaskCompletionSource CallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task<McpClient> ConnectAsync(string _, CancellationToken cancellationToken)
        {
            var input = new Pipe();
            var output = new Pipe();
            var connection = new Connection(this, input.Reader.AsStream(), output.Writer.AsStream());
            lock (_connections) { _connections.Add(connection); }
            Interlocked.Increment(ref Connections);
            connection.Start();
            return await McpClient.CreateAsync(new StreamClientTransport(serverInput: input.Writer.AsStream(), serverOutput: output.Reader.AsStream()),
                new McpClientOptions { ProtocolVersion = "2025-06-18" }, cancellationToken: cancellationToken);
        }

        internal Task NotifyCatalogChangedAsync()
        {
            Connection[] connections;
            lock (_connections) { connections = [.. _connections]; }
            return Task.WhenAll(connections.Select(connection => connection.WriteAsync(new { jsonrpc = "2.0", method = "notifications/tools/list_changed" })));
        }

        public async ValueTask DisposeAsync()
        {
            Connection[] connections;
            lock (_connections) { connections = [.. _connections]; }
            foreach (var connection in connections)
            {
                await connection.DisposeAsync();
            }
        }

        private sealed class Connection(FakeMcpServer server, Stream input, Stream output) : IAsyncDisposable
        {
            private readonly StreamReader _reader = new(input);
            private readonly StreamWriter _writer = new(output, new UTF8Encoding(false)) { AutoFlush = true };
            private readonly SemaphoreSlim _writeGate = new(1, 1);
            private readonly CancellationTokenSource _stop = new();
            private Task? _run;
            private string? _target;

            internal void Start() => _run = RunAsync();

            internal async Task WriteAsync(object response)
            {
                await _writeGate.WaitAsync(_stop.Token);
                try { await _writer.WriteLineAsync(JsonSerializer.Serialize(response, McpJsonUtilities.DefaultOptions)); }
                finally { _writeGate.Release(); }
            }

            private async Task RunAsync()
            {
                try
                {
                    while (await _reader.ReadLineAsync(_stop.Token) is { } line)
                    {
                        using var document = JsonDocument.Parse(line);
                        var request = document.RootElement;
                        if (!request.TryGetProperty("id", out var id))
                        {
                            continue;
                        }

                        object result;
                        switch (request.GetProperty("method").GetString())
                        {
                            case "initialize":
                                result = new { protocolVersion = "2025-06-18", capabilities = new { tools = new { listChanged = true } }, serverInfo = new { name = "fixture", version = "1" } };
                                break;
                            case "tools/list":
                                Interlocked.Increment(ref server.CatalogReads);
                                result = new { tools = server.Tools };
                                break;
                            case "tools/call":
                                var parameters = request.GetProperty("params");
                                if (server.OnToolCall is { } handler)
                                {
                                    result = handler(parameters);
                                    break;
                                }
                                if (parameters.GetProperty("name").GetString() == "bind")
                                {
                                    Interlocked.Increment(ref server.BindCalls);
                                    _target = parameters.GetProperty("arguments").GetProperty("target").GetString();
                                    result = new CallToolResult { Content = [] };
                                    break;
                                }

                                Interlocked.Increment(ref server.ToolCalls);
                                server.CallStarted.TrySetResult();
                                if (server.HoldCalls)
                                {
                                    continue;
                                }

                                if (server.DisconnectCalls)
                                {
                                    await output.DisposeAsync();
                                    return;
                                }

                                var resource = parameters.TryGetProperty("arguments", out var args) && args.TryGetProperty("resource", out var value) ? value.GetString() : null;
                                result = new CallToolResult
                                {
                                    IsError = server.ErrorResult,
                                    Content = [new TextContentBlock { Text = server.Text }],
                                    StructuredContent = server.IncludeStructuredContent ? JsonSerializer.SerializeToElement(new { resource, target = _target }) : null,
                                    Meta = new JsonObject { ["providerField"] = "unchanged" },
                                };
                                break;
                            default:
                                result = new { };
                                break;
                        }

                        await WriteAsync(new { jsonrpc = "2.0", id, result });
                    }
                }
                catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or IOException) { }
            }

            public async ValueTask DisposeAsync()
            {
                await _stop.CancelAsync();
                await input.DisposeAsync();
                if (_run is not null)
                {
                    await _run;
                }

                try { await _writer.DisposeAsync(); }
                catch (Exception error) when (error is ObjectDisposedException or IOException or InvalidOperationException) { }
                await output.DisposeAsync();
                _stop.Dispose();
                _reader.Dispose();
                _writeGate.Dispose();
            }
        }
    }
}
