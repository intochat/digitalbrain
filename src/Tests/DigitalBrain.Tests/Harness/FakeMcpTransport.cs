using System.Text.Json;
using DigitalBrain.Modules.Sdk.Mcp;

namespace DigitalBrain.Tests.Harness;

internal sealed class FakeMcpTransport : IMcpToolTransport
{
    public Task<IReadOnlyList<McpToolDescription>> ListToolsAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken)
    {
        if (string.Equals(server.Key, "google.gmail", StringComparison.OrdinalIgnoreCase)
            || server.Key.Contains("gmail", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<McpToolDescription>>(
            [
                new McpToolDescription(
                    "search_threads",
                    "Search Gmail threads",
                    """{"type":"object","properties":{"query":{"type":"string"}}}""",
                    Destructive: false),
                new McpToolDescription(
                    "get_thread",
                    "Get messages in a thread",
                    """{"type":"object","properties":{"threadId":{"type":"string"}}}""",
                    Destructive: false),
                new McpToolDescription(
                    "create_draft",
                    "Create a draft message",
                    """{"type":"object"}""",
                    Destructive: true),
            ]);
        }

        return Task.FromResult<IReadOnlyList<McpToolDescription>>(
        [
            new McpToolDescription(
                "soqlQuery",
                "Run a SOQL query",
                """{"type":"object","properties":{"query":{"type":"string"}}}""",
                Destructive: false),
            new McpToolDescription(
                "updateSobjectRecord",
                "Update a record",
                """{"type":"object"}""",
                Destructive: true),
        ]);
    }

    public Task<JsonElement> CallToolAsync(
        McpServerDefinition server,
        string tool,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (string.Equals(server.Key, "google.gmail", StringComparison.OrdinalIgnoreCase)
            || server.Key.Contains("gmail", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(JsonDocument.Parse(
                """
                {"threads":[
                    {"id":"t1","snippet":"Hello from Gmail MCP"},
                    {"id":"t2","snippet":"Second thread"}
                ]}
                """).RootElement.Clone());
        }

        return Task.FromResult(JsonDocument.Parse(
            """
            {"records":[
                {"series":"sales","label":"W1","value":100},
                {"series":"sales","label":"W2","value":250}
            ]}
            """).RootElement.Clone());
    }
}
