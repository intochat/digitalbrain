using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var baseUrl = Environment.GetEnvironmentVariable("DIGITALBRAIN_MCP_URL")?.TrimEnd('/')
    ?? throw new InvalidOperationException("Set DIGITALBRAIN_MCP_URL to the mcp HTTPS origin, e.g. https://localhost:58997");
var username = Environment.GetEnvironmentVariable("DIGITALBRAIN_MCP_USERNAME") ?? "admin";
var password = Environment.GetEnvironmentVariable("DIGITALBRAIN_MCP_PASSWORD") ?? "admin";

using var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
var sessionResponse = await http.PostAsJsonAsync(
    $"{baseUrl}/dev/mcp-session",
    new { username, password });
if (!sessionResponse.IsSuccessStatusCode)
{
    var body = await sessionResponse.Content.ReadAsStringAsync();
    throw new InvalidOperationException($"MCP dev session failed ({(int)sessionResponse.StatusCode}): {body}");
}
using var sessionDoc = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
var accessToken = sessionDoc.RootElement.GetProperty("accessToken").GetString()
    ?? throw new InvalidOperationException("MCP session reply missing accessToken.");
var audience = sessionDoc.RootElement.TryGetProperty("audience", out var audienceProperty)
    ? audienceProperty.GetString() ?? "digitalbrain-v3"
    : "digitalbrain-v3";

var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri($"{baseUrl}/mcp"),
    TransportMode = HttpTransportMode.StreamableHttp,
    AdditionalHeaders = new Dictionary<string, string>
    {
        ["Authorization"] = $"Bearer {accessToken}",
        ["X-V2-Audience"] = audience
    }
}, http, ownsHttpClient: false);

await using var remote = await McpClient.CreateAsync(transport);
var remoteTools = await remote.ListToolsAsync();

var server = McpServer.Create(new StdioServerTransport("digitalbrain"), new McpServerOptions
{
    ServerInfo = new Implementation { Name = "digitalbrain", Version = "1.0.0" },
    Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
    Handlers = new McpServerHandlers
    {
        ListToolsHandler = async (_, _) =>
        {
            var tools = new List<Tool>();
            foreach (var tool in remoteTools)
            {
                tools.Add(new Tool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = tool.JsonSchema
                });
            }
            return new ListToolsResult { Tools = tools };
        },
        CallToolHandler = async (request, cancellationToken) =>
        {
            var name = request.Params?.Name ?? throw new ArgumentException("Tool name is required.");
            var arguments = request.Params.Arguments is null
                ? null
                : request.Params.Arguments.ToDictionary(
                    pair => pair.Key,
                    pair => (object?)pair.Value);
            var result = await remote.CallToolAsync(name, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new CallToolResult
            {
                Content = result.Content.ToList(),
                IsError = result.IsError
            };
        }
    }
});

await server.RunAsync();
