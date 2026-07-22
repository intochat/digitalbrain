using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
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

        var googleTokens = FakeOAuth.Options("google-token").TokenCache!;
        var salesforceTokens = FakeOAuth.Options("salesforce-token").TokenCache!;
        var google = new GoogleMcpAuthorization(configuration).CreateOptions(googleTokens);
        var salesforce = new SalesforceMcpAuthorization(configuration).CreateOptions(salesforceTokens);

        Assert.Equal("google-client", google.ClientId);
        Assert.Equal("google-secret", google.ClientSecret);
        Assert.Equal(new Uri("http://localhost:41001/callback"), google.RedirectUri);
        Assert.Contains("https://www.googleapis.com/auth/gmail.readonly", google.Scopes!);
        Assert.NotNull(google.AuthorizationRedirectDelegate);
        Assert.NotNull(google.TokenCache);
        Assert.Same(googleTokens, google.TokenCache);

        Assert.Equal("salesforce-client", salesforce.ClientId);
        Assert.Equal("salesforce-secret", salesforce.ClientSecret);
        Assert.Equal(new Uri("http://localhost:41002/callback"), salesforce.RedirectUri);
        Assert.Contains("mcp_api", salesforce.Scopes!);
        Assert.Contains("refresh_token", salesforce.Scopes!);
        Assert.NotNull(salesforce.AuthorizationRedirectDelegate);
        Assert.NotNull(salesforce.TokenCache);
        Assert.Same(salesforceTokens, salesforce.TokenCache);
    }

    [Fact(DisplayName = "OAuth configuration accepts a neuron-owned token cache")]
    public void OAuthConfigurationAcceptsANeuronOwnedTokenCache()
    {
        Assert.Equal(
            [typeof(ModelContextProtocol.Authentication.ITokenCache)],
            typeof(IGoogleMcpAuthorization)
                .GetMethod(nameof(IGoogleMcpAuthorization.CreateOptions))!
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(ModelContextProtocol.Authentication.ITokenCache)],
            typeof(ISalesforceMcpAuthorization)
                .GetMethod(nameof(ISalesforceMcpAuthorization.CreateOptions))!
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
    }

    [Theory(DisplayName = "private MCP boundaries expose catalog snapshots and bind calls to a schema fingerprint")]
    [InlineData(typeof(IGmailMcpTransport))]
    [InlineData(typeof(ISalesforceMcpTransport))]
    public void PrivateMcpBoundariesExposeCatalogSnapshotsAndBindCallsToASchemaFingerprint(
        Type boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        Assert.Contains(boundary.GetMethods(), method => method.Name == "ReadToolAsync");
        var call = Assert.Single(boundary.GetMethods(), method => method.Name == "CallToolAsync");
        Assert.Contains(
            call.GetParameters(),
            parameter => parameter.Name == "expectedSchemaFingerprint"
                && parameter.ParameterType == typeof(string));
    }

    [Theory(DisplayName = "neuron-owned OAuth tokens survive cache recreation")]
    [InlineData(typeof(Gmail), "DigitalBrain.Google.DurableMcpTokenCache")]
    [InlineData(typeof(SalesforceModule), "DigitalBrain.Salesforce.DurableMcpTokenCache")]
    public async Task NeuronOwnedOAuthTokensSurviveCacheRecreation(
        Type moduleType,
        string cacheTypeName)
    {
        ArgumentNullException.ThrowIfNull(moduleType);

        var state = new FakeDurableValue<byte[]>();
        var writes = 0;
        var protector = new EphemeralDataProtectionProvider().CreateProtector(cacheTypeName);
        var cacheType = moduleType.Assembly.GetType(cacheTypeName, throwOnError: true)!;
        var constructor = Assert.Single(cacheType.GetConstructors(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic));
        ModelContextProtocol.Authentication.ITokenCache CreateCache()
            => Assert.IsAssignableFrom<ModelContextProtocol.Authentication.ITokenCache>(
                constructor.Invoke(
                    [state, (Func<ValueTask>)(() => { writes++; return ValueTask.CompletedTask; }), protector]));
        var stored = new ModelContextProtocol.Authentication.TokenContainer
        {
            TokenType = "Bearer",
            AccessToken = "owner-token",
            RefreshToken = "owner-refresh",
            ExpiresIn = 3600,
            Scope = "owner-scope",
            ObtainedAt = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero),
        };

        await CreateCache().StoreTokensAsync(stored, TestContext.Current.CancellationToken);
        var restored = await CreateCache().GetTokensAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, writes);
        Assert.NotNull(state.Value);
        Assert.DoesNotContain(
            "owner-token",
            Encoding.UTF8.GetString(state.Value),
            StringComparison.Ordinal);
        Assert.Equivalent(stored, restored, strict: true);
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
        Assert.Contains(
            google.Services,
            service => service.ServiceType.FullName == "System.Net.Http.IHttpClientFactory");
        Assert.Contains(
            salesforce.Services,
            service => service.ServiceType.FullName == "System.Net.Http.IHttpClientFactory");
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
        var transport = new GmailMcpTransport(new FakeHttpClientFactory(server));
        var authorization = FakeOAuth.Options("fake-gmail-token");
        var endpoint = new Uri("https://gmailmcp.googleapis.com/mcp/v1");
        var tool = await transport.ReadToolAsync(
            endpoint,
            authorization,
            "get_message",
            TestContext.Current.CancellationToken);

        var result = await transport.CallToolAsync(
            endpoint,
            authorization,
            "get_message",
            new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" },
            tool.SchemaFingerprint,
            TestContext.Current.CancellationToken);

        Assert.Equal("gmail-message-42", result.GetProperty("id").GetString());
        Assert.NotEmpty(server.BearerTokens);
        Assert.All(server.BearerTokens, token => Assert.Equal("fake-gmail-token", token));
        Assert.Equal("get_message", Assert.Single(server.ToolCalls).Tool);
        Assert.Equal(
            ["initialize", "tools/list", "initialize", "tools/list", "tools/call"],
            server.RequestMethods);
    }

    [Fact(DisplayName = "Salesforce transport uses the official MCP SDK over authenticated HTTP")]
    public async Task SalesforceTransportUsesOfficialSdkOverAuthenticatedHttp()
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new { success = true }));
        var transport = new SalesforceMcpTransport(new FakeHttpClientFactory(server));
        var authorization = FakeOAuth.Options("fake-salesforce-token");
        var endpoint = new Uri(
            "https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations");
        var tool = await transport.ReadToolAsync(
            endpoint,
            authorization,
            "update_sobject_record",
            TestContext.Current.CancellationToken);

        var result = await transport.CallToolAsync(
            endpoint,
            authorization,
            "update_sobject_record",
            new Dictionary<string, object?> { ["id"] = "001000000000042AAA" },
            tool.SchemaFingerprint,
            TestContext.Current.CancellationToken);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.NotEmpty(server.BearerTokens);
        Assert.All(server.BearerTokens, token => Assert.Equal("fake-salesforce-token", token));
        Assert.Equal("update_sobject_record", Assert.Single(server.ToolCalls).Tool);
        Assert.Equal(
            ["initialize", "tools/list", "initialize", "tools/list", "tools/call"],
            server.RequestMethods);
    }

    [Fact(DisplayName = "Gmail rejects an incompatible private MCP catalog before invocation")]
    public async Task GmailRejectsIncompatibleCatalogBeforeInvocation()
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new { }))
        {
            AdvertiseInvalidGmailSchema = true,
        };
        var transport = new GmailMcpTransport(new FakeHttpClientFactory(server));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.ReadToolAsync(
                new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
                FakeOAuth.Options("fake-gmail-token"),
                "get_message",
                TestContext.Current.CancellationToken));

        Assert.Contains("get_message", failure.Message, StringComparison.Ordinal);
        Assert.Empty(server.ToolCalls);
    }

    [Fact(DisplayName = "Gmail rejects private MCP schema drift before invocation")]
    public async Task GmailRejectsSchemaDriftBeforeInvocation()
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new { }))
        {
            DriftGmailSchemaAfterAdmission = true,
        };
        var transport = new GmailMcpTransport(new FakeHttpClientFactory(server));
        var endpoint = new Uri("https://gmailmcp.googleapis.com/mcp/v1");
        var authorization = FakeOAuth.Options("fake-gmail-token");
        var tool = await transport.ReadToolAsync(
            endpoint,
            authorization,
            "get_message",
            TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.CallToolAsync(
                endpoint,
                authorization,
                "get_message",
                new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" },
                tool.SchemaFingerprint,
                TestContext.Current.CancellationToken));

        Assert.Contains("schema changed", failure.Message, StringComparison.Ordinal);
        Assert.Empty(server.ToolCalls);
    }

    [Fact(DisplayName = "MCP schema fingerprints ignore JSON object property order")]
    public async Task McpSchemaFingerprintsIgnoreObjectPropertyOrder()
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new
        {
            id = "gmail-message-42",
        }))
        {
            ReorderGmailSchemaAfterAdmission = true,
        };
        var transport = new GmailMcpTransport(new FakeHttpClientFactory(server));
        var endpoint = new Uri("https://gmailmcp.googleapis.com/mcp/v1");
        var authorization = FakeOAuth.Options("fake-gmail-token");
        var tool = await transport.ReadToolAsync(
            endpoint,
            authorization,
            "get_message",
            TestContext.Current.CancellationToken);

        var result = await transport.CallToolAsync(
            endpoint,
            authorization,
            "get_message",
            new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" },
            tool.SchemaFingerprint,
            TestContext.Current.CancellationToken);

        Assert.Equal("gmail-message-42", result.GetProperty("id").GetString());
        Assert.Single(server.ToolCalls);
    }

    [Fact(DisplayName = "Salesforce rejects an incompatible private MCP catalog before invocation")]
    public async Task SalesforceRejectsIncompatibleCatalogBeforeInvocation()
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new { }))
        {
            AdvertiseInvalidSalesforceSchema = true,
        };
        var transport = new SalesforceMcpTransport(new FakeHttpClientFactory(server));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.ReadToolAsync(
                new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
                FakeOAuth.Options("fake-salesforce-token"),
                "update_sobject_record",
                TestContext.Current.CancellationToken));

        Assert.Contains("update_sobject_record", failure.Message, StringComparison.Ordinal);
        Assert.Empty(server.ToolCalls);
    }

    [Fact(DisplayName = "MCP transport cancellation reaches authenticated HTTP")]
    public async Task McpTransportCancellationReachesAuthenticatedHttp()
    {
        using var server = new CancellationProbeHandler();
        using var cancellation = new CancellationTokenSource();
        var transport = new GmailMcpTransport(new FakeHttpClientFactory(server));
        var pending = transport.ReadToolAsync(
            new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
            FakeOAuth.Options("fake-gmail-token"),
            "get_message",
            cancellation.Token).AsTask();

        await server.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}

internal sealed class FakeMcpHttpServer(JsonElement structuredContent) : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> _bearerTokens = new();
    private readonly ConcurrentQueue<McpToolCall> _toolCalls = new();
    private readonly ConcurrentQueue<string> _requestMethods = new();
    private int _catalogReads;

    internal bool AdvertiseInvalidGmailSchema { get; init; }

    internal bool AdvertiseInvalidSalesforceSchema { get; init; }

    internal bool DriftGmailSchemaAfterAdmission { get; init; }

    internal bool ReorderGmailSchemaAfterAdmission { get; init; }

    internal IReadOnlyList<string> BearerTokens => [.. _bearerTokens];

    internal IReadOnlyList<McpToolCall> ToolCalls => [.. _toolCalls];

    internal IReadOnlyList<string> RequestMethods => [.. _requestMethods];

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

            _requestMethods.Enqueue(methodName!);

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
                "tools/list" => new
                {
                    tools = Tools(),
                },
                "tools/call" => ToolResult(payload.RootElement),
                _ => throw new InvalidOperationException($"Unexpected MCP method '{methodName}'."),
            };

            return Json(new { jsonrpc = "2.0", id, result });
        }
    }

    private object[] Tools()
    {
        var catalogRead = Interlocked.Increment(ref _catalogReads);
        var invalidGmail = AdvertiseInvalidGmailSchema
            || (DriftGmailSchemaAfterAdmission && catalogRead > 1);

        return
        [
            invalidGmail
                ? Tool("get_message", readOnly: true, "messageFormat")
                : ReorderGmailSchemaAfterAdmission && catalogRead > 1
                    ? ReorderedGmailTool()
                    : Tool("get_message", readOnly: true, "messageId", "messageFormat"),
            AdvertiseInvalidSalesforceSchema
                ? Tool("update_sobject_record", readOnly: false, "sobject-name", "id")
                : Tool("update_sobject_record", readOnly: false, "sobject-name", "id", "body"),
            Tool("soqlQuery", readOnly: true, "query"),
        ];
    }

    private static object ReorderedGmailTool() => new
    {
        name = "get_message",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["messageFormat"] = new { type = "string" },
                ["messageId"] = new { type = "string" },
            },
            required = new[] { "messageId", "messageFormat" },
        },
        annotations = new
        {
            readOnlyHint = true,
            destructiveHint = false,
        },
    };

    private static object Tool(string name, bool readOnly, params string[] properties) => new
    {
        name,
        inputSchema = new
        {
            type = "object",
            properties = properties.ToDictionary(
                property => property,
                property => (object)new { type = property == "body" ? "object" : "string" },
                StringComparer.Ordinal),
            required = properties,
        },
        annotations = new
        {
            readOnlyHint = readOnly,
            destructiveHint = false,
        },
    };

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

internal sealed class CancellationProbeHandler : HttpMessageHandler
{
    internal TaskCompletionSource Entered { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The cancellation probe unexpectedly resumed.");
    }
}

internal sealed record McpToolCall(string Tool, JsonElement Arguments);

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class FakeDurableValue<T> : IDurableValue<T>
{
    public T? Value { get; set; }
}

internal sealed class ProbeSiloBuilder : ISiloBuilder
{
    public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();

    public IServiceCollection Services { get; } = new ServiceCollection();
}
