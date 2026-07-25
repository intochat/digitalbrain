using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Mcp;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Product edge constants are the single source for MCP host, AppHost value-match, and tests.")]
public static class McpHost
{
    public const string ResourceName = "digitalbrain-mcp";
    public const string EndpointPath = "/mcp";
    public const string HealthPath = "/health";
    public const string HealthResponse = "healthy";
    public const string HttpEndpointName = "http";
    public const int HttpPort = 5000;
    public const string AskLlama32ToolName = "ask_llama32";
    public const string DefaultLlama32Key = "default";

    public static WebApplication MapMcpHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(HealthPath, static () => Results.Ok(HealthResponse));
        app.MapMcp(EndpointPath);
        return app;
    }
}
