using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class OfficialMcpTransportContracts
{
    [Fact(DisplayName = "official OAuth options come only from projected configuration")]
    public void OfficialOAuthOptionsComeOnlyFromProjectedConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Google:Gmail:ClientId"] = "google-client",
                ["DigitalBrain:Google:Gmail:ClientSecret"] = "google-secret",
                ["DigitalBrain:Google:Gmail:RedirectUri"] = "http://localhost:41001/callback",
                ["DigitalBrain:Salesforce:ClientId"] = "salesforce-client",
                ["DigitalBrain:Salesforce:ClientSecret"] = "salesforce-secret",
                ["DigitalBrain:Salesforce:RedirectUri"] = "http://localhost:41002/callback",
            })
            .Build();

        var google = new GoogleMcpAuthorization(configuration).CreateOptions();
        var salesforce = new SalesforceMcpAuthorization(configuration).CreateOptions();

        Assert.Equal("google-client", google.ClientId);
        Assert.Equal("google-secret", google.ClientSecret);
        Assert.Equal(new Uri("http://localhost:41001/callback"), google.RedirectUri);
        Assert.Contains("https://www.googleapis.com/auth/gmail.readonly", google.Scopes!);
        Assert.NotNull(google.AuthorizationRedirectDelegate);
        Assert.NotNull(google.TokenCache);

        Assert.Equal("salesforce-client", salesforce.ClientId);
        Assert.Equal("salesforce-secret", salesforce.ClientSecret);
        Assert.Equal(new Uri("http://localhost:41002/callback"), salesforce.RedirectUri);
        Assert.Contains("mcp_api", salesforce.Scopes!);
        Assert.Contains("refresh_token", salesforce.Scopes!);
        Assert.NotNull(salesforce.AuthorizationRedirectDelegate);
        Assert.NotNull(salesforce.TokenCache);
    }

    [Fact(DisplayName = "Google and Salesforce modules own their production OAuth and MCP adapters")]
    public void ModulesOwnTheirProductionOAuthAndMcpAdapters()
    {
        var google = new ProbeSiloBuilder();
        var salesforce = new ProbeSiloBuilder();

        GoogleModule.Configure(google);
        SalesforceModule.Configure(salesforce);

        using var googleServices = google.Services.BuildServiceProvider();
        using var salesforceServices = salesforce.Services.BuildServiceProvider();

        Assert.IsType<GoogleMcpAuthorization>(
            googleServices.GetRequiredService<IGoogleMcpAuthorization>());
        Assert.IsType<GmailMcpTransport>(
            googleServices.GetRequiredService<IGmailMcpTransport>());
        Assert.IsType<SalesforceMcpAuthorization>(
            salesforceServices.GetRequiredService<ISalesforceMcpAuthorization>());
        Assert.IsType<SalesforceMcpTransport>(
            salesforceServices.GetRequiredService<ISalesforceMcpTransport>());
    }

    [Fact(DisplayName = "Gmail transport uses the official MCP SDK over authenticated HTTP")]
    public async Task GmailTransportUsesOfficialSdkOverAuthenticatedHttp()
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new
        {
            id = "gmail-message-42",
            subject = "Pilot rollout",
            sender = "priya@northstar.example",
            plaintextBody = "Ready.",
        }));
        using var http = new HttpClient(server);
        using var transport = new GmailMcpTransport(http);

        var result = await transport.CallToolAsync(
            new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
            FakeOAuth.Options("fake-gmail-token"),
            "get_message",
            new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" },
            TestContext.Current.CancellationToken);

        Assert.Equal("gmail-message-42", result.GetProperty("id").GetString());
        Assert.NotEmpty(server.BearerTokens);
        Assert.All(server.BearerTokens, token => Assert.Equal("fake-gmail-token", token));
        Assert.Equal("get_message", Assert.Single(server.ToolCalls).Tool);
    }

    [Fact(DisplayName = "Salesforce transport uses the official MCP SDK over authenticated HTTP")]
    public async Task SalesforceTransportUsesOfficialSdkOverAuthenticatedHttp()
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new { success = true }));
        using var http = new HttpClient(server);
        using var transport = new SalesforceMcpTransport(http);

        var result = await transport.CallToolAsync(
            new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
            FakeOAuth.Options("fake-salesforce-token"),
            "update_sobject_record",
            new Dictionary<string, object?> { ["id"] = "001000000000042AAA" },
            TestContext.Current.CancellationToken);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.NotEmpty(server.BearerTokens);
        Assert.All(server.BearerTokens, token => Assert.Equal("fake-salesforce-token", token));
        Assert.Equal("update_sobject_record", Assert.Single(server.ToolCalls).Tool);
    }
}

internal sealed class FakeMcpHttpServer(JsonElement structuredContent) : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> _bearerTokens = new();
    private readonly ConcurrentQueue<McpToolCall> _toolCalls = new();

    internal IReadOnlyList<string> BearerTokens => [.. _bearerTokens];

    internal IReadOnlyList<McpToolCall> ToolCalls => [.. _toolCalls];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } token })
        {
            _bearerTokens.Enqueue(token);
        }

        if (request.Method == HttpMethod.Delete)
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        var payload = request.Content is null
            ? null
            : await JsonDocument.ParseAsync(
                await request.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        using (payload)
        {
            if (payload?.RootElement.TryGetProperty("method", out var method) is not true)
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            var methodName = method.GetString();

            if (methodName == "notifications/initialized")
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            var id = payload.RootElement.GetProperty("id").Clone();
            object result = methodName switch
            {
                "initialize" => new
                {
                    protocolVersion = payload.RootElement
                        .GetProperty("params")
                        .GetProperty("protocolVersion")
                        .GetString(),
                    capabilities = new { },
                    serverInfo = new { name = "fake-mcp", version = "1.0" },
                },
                "tools/call" => ToolResult(payload.RootElement),
                _ => throw new InvalidOperationException($"Unexpected MCP method '{methodName}'."),
            };

            return Json(new { jsonrpc = "2.0", id, result });
        }
    }

    private object ToolResult(JsonElement request)
    {
        var parameters = request.GetProperty("params");
        _toolCalls.Enqueue(new(
            parameters.GetProperty("name").GetString()!,
            parameters.GetProperty("arguments").Clone()));

        return new
        {
            content = Array.Empty<object>(),
            structuredContent,
            isError = false,
        };
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(value),
            Encoding.UTF8,
            "application/json"),
    };
}

internal sealed record McpToolCall(string Tool, JsonElement Arguments);

internal sealed class ProbeSiloBuilder : ISiloBuilder
{
    public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();

    public IServiceCollection Services { get; } = new ServiceCollection();
}
