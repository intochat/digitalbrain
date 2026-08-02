using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class NorthboundMcpSurface(TestingAppHostFixture fixture)
{
    private const string McpPath = "/mcp";

    [Fact(
        Timeout = 300_000,
        DisplayName =
            "Silo hosts northbound MCP at /mcp and answers initialize (folded from OS.AgentTools)")]
    public async Task SiloHostsNorthboundMcpInitialize()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);
        var silo = host.Resource(TestingAppHostFixture.SiloResourceName);
        await silo.WaitUntilHealthyAsync(cancellationToken);

        using var client = silo.CreateHttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        using var request = new HttpRequestMessage(HttpMethod.Post, McpPath)
        {
            Content = new StringContent(
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"host-tests","version":"1.0"}}}
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(
            response.IsSuccessStatusCode,
            $"MCP initialize failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        Assert.Contains("result", body, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(ExtractJsonRpcPayload(body));
        Assert.True(document.RootElement.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("serverInfo", out var serverInfo));
        Assert.True(serverInfo.TryGetProperty("name", out _));
    }

    private static string ExtractJsonRpcPayload(string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        if (body.TrimStart().StartsWith('{', StringComparison.Ordinal))
        {
            return body;
        }

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return line["data:".Length..].Trim();
            }
        }

        return body;
    }
}
