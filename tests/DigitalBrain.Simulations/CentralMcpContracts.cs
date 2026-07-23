using System.Text;
using System.Text.Json;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class CentralMcpContracts
{
    [Fact(DisplayName = "MCP authorization is fail-closed unless the host owns an authenticated edge")]
    public async Task AuthorizationIsFailClosedAndHostOwned()
    {
        var services = new ServiceCollection();
        McpRuntimeHosting.Configure(services, new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        var redirect = provider.GetRequiredService<IMcpAuthorizationRedirect>();

        Assert.Same(RejectingMcpAuthorizationRedirect.Instance, redirect);
        await Assert.ThrowsAsync<InvalidOperationException>(() => redirect.AuthorizeAsync(
            new Uri("https://authorization.example/authorize?state=expected"),
            new Uri("https://application.example/callback"),
            TestContext.Current.CancellationToken));

        var hostRedirect = new StubAuthorizationRedirect();
        var hostServices = new ServiceCollection();
        hostServices.AddSingleton<IMcpAuthorizationRedirect>(hostRedirect);
        McpRuntimeHosting.Configure(hostServices, new ConfigurationBuilder().Build());
        await using var hostProvider = hostServices.BuildServiceProvider();

        Assert.Same(hostRedirect, hostProvider.GetRequiredService<IMcpAuthorizationRedirect>());
    }

    [Theory(DisplayName = "local MCP authorization accepts only explicit HTTP loopback callbacks with OAuth state")]
    [InlineData("https://localhost:41001/callback", "https://authorization.example/authorize?state=expected")]
    [InlineData("http://application.example/callback", "https://authorization.example/authorize?state=expected")]
    [InlineData("http://localhost:41001/callback", "https://authorization.example/authorize")]
    public async Task LocalAuthorizationRejectsUnsafeCallbacks(string redirect, string authorization)
    {
        var adapter = new LocalLoopbackMcpAuthorizationRedirect();

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.AuthorizeAsync(
            new Uri(authorization),
            new Uri(redirect),
            TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "shared OAuth options come only from a provider definition and projected configuration")]
    public void OAuthOptionsComeFromDefinitionAndConfiguration()
    {
        var definition = Server(
            "google.gmail",
            "DigitalBrain:Google:Gmail",
            "https://gmailmcp.googleapis.com/mcp/v1",
            "https://www.googleapis.com/auth/gmail.readonly");
        var configuration = Configuration(
            definition.ConfigurationRoot,
            "google-client",
            "google-secret",
            "http://localhost:41001/callback");
        var tokens = FakeOAuth.Options("owner-token").TokenCache!;

        var options = McpOAuthOptions.Create(
            definition,
            configuration,
            tokens,
            RejectingMcpAuthorizationRedirect.Instance);

        Assert.Equal("google-client", options.ClientId);
        Assert.Equal("google-secret", options.ClientSecret);
        Assert.Equal(new Uri("http://localhost:41001/callback"), options.RedirectUri);
        Assert.Equal(definition.Scopes, options.Scopes);
        Assert.Same(tokens, options.TokenCache);
        Assert.NotNull(options.AuthorizationRedirectDelegate);
    }

    [Fact(DisplayName = "shared encrypted OAuth tokens survive cache and protector recreation")]
    public async Task OAuthTokensSurviveCacheAndProtectorRecreation()
    {
        var state = new FakeDurableValue<byte[]>();
        var writes = 0;
        var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var stored = new TokenContainer
        {
            TokenType = "Bearer",
            AccessToken = "owner-token",
            RefreshToken = "owner-refresh",
            ExpiresIn = 3600,
            Scope = "owner-scope",
            ObtainedAt = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero),
        };
        DurableMcpTokenCache CreateCache() => new(
            state,
            () =>
            {
                writes++;
                return ValueTask.CompletedTask;
            },
            new DurablePayloadProtector(key),
            "mcp/oauth/google.gmail/owner-17");

        await CreateCache().StoreTokensAsync(stored, TestContext.Current.CancellationToken);
        var restored = await CreateCache().GetTokensAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, writes);
        Assert.NotNull(state.Value);
        Assert.DoesNotContain("owner-token", Encoding.UTF8.GetString(state.Value), StringComparison.Ordinal);
        Assert.Equivalent(stored, restored, strict: true);
    }

    [Theory(DisplayName = "one official MCP adapter serves read-only and mutation provider policies")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OneOfficialAdapterServesProviderPolicies(bool gmail)
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(
            gmail ? (object)new { id = "gmail-message-42" } : new { success = true }));
        var definition = gmail
            ? Server("google.gmail", "DigitalBrain:Google:Gmail", "https://gmailmcp.googleapis.com/mcp/v1", "gmail.readonly")
            : Server("salesforce", "DigitalBrain:Salesforce", "https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations", "mcp_api");
        var contract = gmail
            ? McpToolContract.ReadOnly(
                "get_message",
                new McpToolProperty("messageId", "string"))
            : McpToolContract.Mutation(
                "update_sobject_record",
                new McpToolProperty("sobject-name", "string"),
                new McpToolProperty("id", "string"),
                new McpToolProperty("body", "object"));
        var client = new SdkMcpClient(
            definition,
            FakeOAuth.Options("provider-token"),
            new FakeHttpClientFactory(server));

        var admitted = await client.InspectAsync(contract, TestContext.Current.CancellationToken);
        var result = await client.InvokeAsync(
            admitted,
            gmail
                ? new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" }
                : new Dictionary<string, object?>
                {
                    ["sobject-name"] = "Account",
                    ["id"] = "001000000000042AAA",
                    ["body"] = new Dictionary<string, object?> { ["Description"] = "Ready" },
                },
            TestContext.Current.CancellationToken);

        Assert.Equal(gmail ? "gmail-message-42" : "True", gmail
            ? result.GetProperty("id").GetString()
            : result.GetProperty("success").GetBoolean().ToString());
        Assert.NotEmpty(server.BearerTokens);
        Assert.All(server.BearerTokens, token => Assert.Equal("provider-token", token));
        Assert.Equal(contract.Name, Assert.Single(server.ToolCalls).Tool);
    }

    [Fact(DisplayName = "shared MCP admission rejects incompatible and drifted schemas before invocation")]
    public async Task AdmissionRejectsIncompatibleAndDriftedSchemas()
    {
        using var incompatible = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new { }))
        {
            AdvertiseInvalidGmailSchema = true,
        };
        var contract = McpToolContract.ReadOnly(
            "get_message",
            new McpToolProperty("messageId", "string"));
        var incompatibleClient = Client(incompatible);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await incompatibleClient.InspectAsync(contract, TestContext.Current.CancellationToken));
        Assert.Empty(incompatible.ToolCalls);

        using var drifted = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new { }))
        {
            DriftGmailSchemaAfterAdmission = true,
        };
        var driftedClient = Client(drifted);
        var admitted = await driftedClient.InspectAsync(contract, TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await driftedClient.InvokeAsync(
                admitted,
                new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" },
                TestContext.Current.CancellationToken));

        Assert.Contains("schema changed", failure.Message, StringComparison.Ordinal);
        Assert.Empty(drifted.ToolCalls);
    }

    [Fact(DisplayName = "shared MCP fingerprints ignore JSON object property order")]
    public async Task FingerprintsIgnoreObjectPropertyOrder()
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new
        {
            id = "gmail-message-42",
        }))
        {
            ReorderGmailSchemaAfterAdmission = true,
        };
        var client = Client(server);
        var admitted = await client.InspectAsync(
            McpToolContract.ReadOnly(
                "get_message",
                new McpToolProperty("messageId", "string")),
            TestContext.Current.CancellationToken);

        var result = await client.InvokeAsync(
            admitted,
            new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" },
            TestContext.Current.CancellationToken);

        Assert.Equal("gmail-message-42", result.GetProperty("id").GetString());
        Assert.Single(server.ToolCalls);
    }

    [Theory(DisplayName = "shared MCP calls reject protocol errors and missing structured content")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CallsRejectErrorsAndMissingStructuredContent(bool isError, bool omitStructuredContent)
    {
        using var server = new FakeMcpHttpServer(JsonSerializer.SerializeToElement(new { }))
        {
            ToolResultIsError = isError,
            OmitStructuredContent = omitStructuredContent,
        };
        var client = Client(server);
        var admitted = await client.InspectAsync(
            McpToolContract.ReadOnly(
                "get_message",
                new McpToolProperty("messageId", "string")),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.InvokeAsync(
                admitted,
                new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" },
                TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "shared MCP cancellation reaches authenticated HTTP")]
    public async Task CancellationReachesAuthenticatedHttp()
    {
        using var server = new CancellationProbeHandler();
        using var cancellation = new CancellationTokenSource();
        var client = new SdkMcpClient(
            Server(
                "google.gmail",
                "DigitalBrain:Google:Gmail",
                "https://gmailmcp.googleapis.com/mcp/v1",
                "gmail.readonly"),
            FakeOAuth.Options("provider-token"),
            new FakeHttpClientFactory(server));
        var pending = client.InspectAsync(
            McpToolContract.ReadOnly(
                "get_message",
                new McpToolProperty("messageId", "string")),
            cancellation.Token).AsTask();

        await server.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static SdkMcpClient Client(HttpMessageHandler handler)
        => new(
            Server(
                "google.gmail",
                "DigitalBrain:Google:Gmail",
                "https://gmailmcp.googleapis.com/mcp/v1",
                "gmail.readonly"),
            FakeOAuth.Options("provider-token"),
            new FakeHttpClientFactory(handler));

    private static McpServerDefinition Server(
        string key,
        string configurationRoot,
        string endpoint,
        params string[] scopes)
        => new(key, $"DigitalBrain {key}", new Uri(endpoint), configurationRoot, scopes);

    private static IConfiguration Configuration(
        string root,
        string clientId,
        string clientSecret,
        string redirectUri)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{root}:ClientId"] = clientId,
                [$"{root}:ClientSecret"] = clientSecret,
                [$"{root}:RedirectUri"] = redirectUri,
            })
            .Build();

    private sealed class StubAuthorizationRedirect : IMcpAuthorizationRedirect
    {
        public Task<string?> AuthorizeAsync(
            Uri authorizationUri,
            Uri redirectUri,
            CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }
}
