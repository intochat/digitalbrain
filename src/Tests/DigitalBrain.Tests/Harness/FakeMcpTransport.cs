using System.Text.Json;
using DigitalBrain.Modules.Sdk.Mcp;

namespace DigitalBrain.Tests.Harness;

internal sealed class FakeMcpTransport : IMcpToolTransport
{
    public Task<IReadOnlyList<McpToolDescription>> ListToolsAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<McpToolDescription>>(
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

    public Task<JsonElement> CallToolAsync(
        McpServerDefinition server,
        string tool,
        JsonElement arguments,
        CancellationToken cancellationToken)
        => Task.FromResult(JsonDocument.Parse(
            """
            {"records":[
                {"series":"sales","label":"W1","value":100},
                {"series":"sales","label":"W2","value":250}
            ]}
            """).RootElement.Clone());
}
