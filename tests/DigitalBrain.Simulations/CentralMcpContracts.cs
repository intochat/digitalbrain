using System.Text;
using System.Text.Json;
using DigitalBrain.Google;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class CentralMcpContracts
{
    [Fact(DisplayName = "MCP authorization is fail-closed unless local loopback is explicit")]
    public async Task AuthorizationIsFailClosedUnlessLocalLoopbackIsExplicit()
    {
        var reject = McpAuthorizationRedirect.Create(new ConfigurationBuilder().Build());
        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => reject(
            new Uri("https://authorization.example/authorize?state=expected"),
            new Uri("https://application.example/callback"),
            TestContext.Current.CancellationToken));
        Assert.Contains("disabled", rejected.Message, StringComparison.Ordinal);

        var local = McpAuthorizationRedirect.Create(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [McpRuntimeHosting.AuthorizationModeKey] = McpRuntimeHosting.LocalLoopbackDevelopmentMode,
            })
            .Build());
        var unsafeRedirect = await Assert.ThrowsAsync<InvalidOperationException>(() => local(
            new Uri("https://authorization.example/authorize?state=expected"),
            new Uri("https://localhost:41001/callback"),
            TestContext.Current.CancellationToken));
        Assert.Contains("HTTP loopback", unsafeRedirect.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "MCP authorization exposes no public redirect override seam")]
    public void AuthorizationRedirectIsPrivate()
    {
        Assert.DoesNotContain(
            typeof(McpRuntimeHosting).Assembly.GetExportedTypes(),
            type => type.Name == "IMcpAuthorizationRedirect");
    }

    [Theory(DisplayName = "local MCP authorization accepts only explicit HTTP loopback callbacks with OAuth state")]
    [InlineData("https://localhost:41001/callback", "https://authorization.example/authorize?state=expected")]
    [InlineData("http://application.example/callback", "https://authorization.example/authorize?state=expected")]
    [InlineData("http://localhost:41001/callback", "https://authorization.example/authorize")]
    public async Task LocalAuthorizationRejectsUnsafeCallbacks(string redirect, string authorization)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LocalLoopbackMcpAuthorizationRedirect.AuthorizeAsync(
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
            tokens);

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

    [Fact(DisplayName = "MCP token state rolls back when its durable commit fails")]
    public async Task OAuthTokenStateRollsBackWhenCommitFails()
    {
        var previous = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var state = new FakeDurableValue<byte[]> { Value = previous };
        var cache = new DurableMcpTokenCache(
            state,
            () => ValueTask.FromException(new InvalidOperationException("commit failed")),
            new DurablePayloadProtector(NewEncodedKey()),
            "mcp/oauth/google.gmail/gmail:owner/account@example.com");
        var replacement = await FakeOAuth.Options("replacement-token").TokenCache!
            .GetTokensAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await cache.StoreTokensAsync(
                replacement!,
                TestContext.Current.CancellationToken));

        Assert.Same(previous, state.Value);
    }

    [Theory(DisplayName = "explicit loopback authorization rejects callbacks outside the SDK redirect contract")]
    [InlineData(
        "http://localhost:41001/callback",
        "https://authorization.example/authorize?state=sdk-state",
        "http://localhost:41001/wrong?code=code&state=sdk-state")]
    [InlineData(
        "http://localhost:41001/callback",
        "https://authorization.example/authorize?state=sdk-state",
        "http://localhost:41001/callback?code=code&state=wrong-state")]
    public void LoopbackAuthorizationRejectsWrongPathOrReturnedState(
        string redirect,
        string authorization,
        string callback)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ValidateLoopbackCallback(authorization, redirect, callback));
    }

    [Fact(DisplayName = "explicit loopback authorization accepts the SDK-supplied state")]
    public void LoopbackAuthorizationAcceptsSdkState()
    {
        var code = ValidateLoopbackCallback(
            "https://authorization.example/authorize?state=sdk-state",
            "http://localhost:41001/callback",
            "http://localhost:41001/callback?code=authorization-code&state=sdk-state");

        Assert.Equal("authorization-code", code);
    }

    [Fact(DisplayName = "Salesforce PKCE OAuth accepts an omitted client secret while Gmail does not")]
    public void ProviderClientSecretRequirementsAreExact()
    {
        var salesforce = new McpServerDefinition(
            "salesforce",
            "DigitalBrain salesforce",
            new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
            "DigitalBrain:Salesforce",
            ["mcp_api", "refresh_token"],
            requiresClientSecret: false);
        var salesforceOptions = McpOAuthOptions.Create(
            salesforce,
            Configuration(
                salesforce.ConfigurationRoot,
                "salesforce-client",
                clientSecret: null,
                redirectUri: "http://localhost:41001/callback"),
            FakeOAuth.Options("owner-token").TokenCache!);

        Assert.Null(salesforceOptions.ClientSecret);

        var gmail = Server(
            "google.gmail",
            "DigitalBrain:Google:Gmail",
            "https://gmailmcp.googleapis.com/mcp/v1",
            "https://www.googleapis.com/auth/gmail.readonly");
        Assert.Throws<InvalidOperationException>(() => McpOAuthOptions.Create(
            gmail,
            Configuration(
                gmail.ConfigurationRoot,
                "google-client",
                clientSecret: null,
                redirectUri: "http://localhost:41001/callback"),
            FakeOAuth.Options("owner-token").TokenCache!));
    }

    [Fact(DisplayName = "one callback-scoped official MCP client sends its preloaded bearer and cannot escape alive")]
    public async Task CallbackScopedRuntimeSendsBearerAndDisposesClient()
    {
        using var server = GmailServer();
        var definition = GmailDefinition();
        var state = new FakeDurableValue<byte[]>();
        var protector = new DurablePayloadProtector(NewEncodedKey());
        var cache = new DurableMcpTokenCache(
            state,
            () => ValueTask.CompletedTask,
            protector,
            McpRuntime.TokenPurpose(definition, "gmail:owner/account@example.com"));
        var token = await FakeOAuth.Options("provider-token").TokenCache!
            .GetTokensAsync(TestContext.Current.CancellationToken);
        await cache.StoreTokensAsync(token!, TestContext.Current.CancellationToken);
        var runtime = Runtime(server, definition, protector);
        McpClient? escaped = null;

        var result = await runtime.RunAsync(
            definition,
            state,
            () => ValueTask.CompletedTask,
            "gmail:owner/account@example.com",
            async (client, cancellationToken) =>
            {
                escaped = client;
                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                var tool = Gmail.AdmitGetMessage(tools);
                var called = await tool.CallAsync(
                    new Dictionary<string, object?>
                    {
                        ["messageId"] = "gmail-message-42",
                        ["messageFormat"] = "FULL_CONTENT",
                    },
                    cancellationToken: cancellationToken);
                return McpRuntime.RequireStructuredContent(called, definition, tool.Name);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("gmail-message-42", result.GetProperty("id").GetString());
        Assert.NotEmpty(server.BearerTokens);
        Assert.All(server.BearerTokens, bearer => Assert.Equal("provider-token", bearer));
        Assert.Equal("get_message", Assert.Single(server.ToolCalls).Tool);
        var requestCount = server.RequestMethods.Count;
        var escapedFailure = await Record.ExceptionAsync(async () =>
            await escaped!.PingAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotNull(escapedFailure);
        Assert.Equal(requestCount, server.RequestMethods.Count);
    }

    [Theory(DisplayName = "Gmail admits only the exact hosted get_message contract")]
    [InlineData((int)GmailToolFault.Name)]
    [InlineData((int)GmailToolFault.InputNonObject)]
    [InlineData((int)GmailToolFault.MessageIdType)]
    [InlineData((int)GmailToolFault.MessageFormatType)]
    [InlineData((int)GmailToolFault.MessageFormatEnum)]
    [InlineData((int)GmailToolFault.RequiredInputs)]
    [InlineData((int)GmailToolFault.OutputSchemaMissing)]
    [InlineData((int)GmailToolFault.OutputNonObject)]
    [InlineData((int)GmailToolFault.OutputIdMissing)]
    [InlineData((int)GmailToolFault.OutputIdType)]
    [InlineData((int)GmailToolFault.OutputSubjectMissing)]
    [InlineData((int)GmailToolFault.OutputSubjectType)]
    [InlineData((int)GmailToolFault.OutputSenderMissing)]
    [InlineData((int)GmailToolFault.OutputSenderType)]
    [InlineData((int)GmailToolFault.OutputPlaintextBodyMissing)]
    [InlineData((int)GmailToolFault.OutputPlaintextBodyType)]
    [InlineData((int)GmailToolFault.AnnotationsMissing)]
    [InlineData((int)GmailToolFault.ReadOnly)]
    [InlineData((int)GmailToolFault.Destructive)]
    [InlineData((int)GmailToolFault.Idempotent)]
    [InlineData((int)GmailToolFault.OpenWorld)]
    public async Task GmailRejectsHostedToolContractDrift(int faultValue)
    {
        using var server = GmailServer((GmailToolFault)faultValue);
        var definition = GmailDefinition();
        var runtime = Runtime(server, definition);

        var rejection = await Record.ExceptionAsync(async () =>
            await runtime.RunAsync(
                definition,
                new FakeDurableValue<byte[]>(),
                () => ValueTask.CompletedTask,
                "gmail:owner/account@example.com",
                async (client, cancellationToken) =>
                {
                    var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                    _ = Gmail.AdmitGetMessage(tools);
                    return true;
                },
                TestContext.Current.CancellationToken).AsTask());

        Assert.True(rejection is InvalidOperationException or ArgumentException);
        Assert.Empty(server.ToolCalls);
    }

    [Fact(DisplayName = "shared MCP fingerprints ignore JSON object property order")]
    public void FingerprintsIgnoreObjectPropertyOrder()
    {
        using var first = JsonDocument.Parse("""{"type":"object","properties":{"b":{"type":"string"},"a":{"type":"number"}}}""");
        using var second = JsonDocument.Parse("""{"properties":{"a":{"type":"number"},"b":{"type":"string"}},"type":"object"}""");

        Assert.Equal(
            McpToolFingerprint.Create(first.RootElement, null, true, false, true, false),
            McpToolFingerprint.Create(second.RootElement, null, true, false, true, false));
    }

    [Theory(DisplayName = "shared MCP calls reject protocol errors and missing structured content")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CallsRejectErrorsAndMissingStructuredContent(bool isError, bool omitStructuredContent)
    {
        using var server = GmailServer();
        server.ToolResultIsError = isError;
        server.OmitStructuredContent = omitStructuredContent;
        var definition = GmailDefinition();
        var runtime = Runtime(server, definition);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.RunAsync(
                definition,
                new FakeDurableValue<byte[]>(),
                () => ValueTask.CompletedTask,
                "gmail:owner/account@example.com",
                async (client, cancellationToken) =>
                {
                    var tool = Gmail.AdmitGetMessage(
                        await client.ListToolsAsync(cancellationToken: cancellationToken));
                    var result = await tool.CallAsync(
                        new Dictionary<string, object?> { ["messageId"] = "gmail-message-42" },
                        cancellationToken: cancellationToken);
                    return McpRuntime.RequireStructuredContent(result, definition, tool.Name);
                },
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact(DisplayName = "shared MCP cancellation reaches authenticated HTTP")]
    public async Task CancellationReachesAuthenticatedHttp()
    {
        using var server = new CancellationProbeHandler();
        using var cancellation = new CancellationTokenSource();
        var definition = GmailDefinition();
        var runtime = Runtime(server, definition);
        var pending = runtime.RunAsync(
            definition,
            new FakeDurableValue<byte[]>(),
            () => ValueTask.CompletedTask,
            "gmail:owner/account@example.com",
            static (_, _) => ValueTask.FromResult(true),
            cancellation.Token).AsTask();

        await server.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static FakeMcpHttpServer GmailServer(GmailToolFault fault = GmailToolFault.None)
        => new(JsonSerializer.SerializeToElement(new
        {
            id = "gmail-message-42",
            subject = "Pilot rollout",
            sender = "priya@northstar.example",
            plaintextBody = "We are ready to start the pilot on Monday.",
        }))
        {
            GmailFault = fault,
        };

    private static McpServerDefinition GmailDefinition() =>
        Server(
            "google.gmail",
            "DigitalBrain:Google:Gmail",
            "https://gmailmcp.googleapis.com/mcp/v1",
            "gmail.readonly");

    private static McpRuntime Runtime(
        HttpMessageHandler handler,
        McpServerDefinition server,
        DurablePayloadProtector? protector = null) =>
        new(
            Configuration(
                server.ConfigurationRoot,
                "fake-client",
                "fake-secret",
                "http://localhost/fake-callback"),
            new FakeHttpClientFactory(handler),
            protector ?? new DurablePayloadProtector(NewEncodedKey()));

    private static McpServerDefinition Server(
        string key,
        string configurationRoot,
        string endpoint,
        params string[] scopes)
        => new(key, $"DigitalBrain {key}", new Uri(endpoint), configurationRoot, scopes);

    private static IConfiguration Configuration(
        string root,
        string clientId,
        string? clientSecret,
        string redirectUri)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{root}:ClientId"] = clientId,
                [$"{root}:ClientSecret"] = clientSecret,
                [$"{root}:RedirectUri"] = redirectUri,
            })
            .Build();

    private static string ValidateLoopbackCallback(
        string authorization,
        string redirect,
        string callback)
    {
        return LocalLoopbackMcpAuthorizationRedirect.ValidateCallback(
            new Uri(authorization),
            new Uri(redirect),
            new Uri(callback));
    }

    private static string NewEncodedKey() =>
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
}
