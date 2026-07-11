using System.Net;
using System.Text;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Tests.Integrations;

public sealed class OAuthConnectorSecurityTests
{
    [Fact]
    public async Task Google_oauth_state_and_tokens_are_principal_scoped()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"refresh_token\":\"refresh-a\",\"expires_in\":3600}");
        var connector = new GoogleConnector(store, new FakeOAuthStateProtector(), tokenEndpointHandler: handler);
        var first = new NeuronId("principal-a");
        var second = new NeuronId("principal-b");

        var firstChallenge = await connector.BeginAuthAsync(first);
        var secondChallenge = await connector.BeginAuthAsync(second);

        Assert.False(firstChallenge.IsForm);
        Assert.False(secondChallenge.IsForm);
        Assert.NotEqual(firstChallenge.State, secondChallenge.State);
        Assert.DoesNotContain(first.Value, firstChallenge.State!, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Value, secondChallenge.State!, StringComparison.Ordinal);

        var malformed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: "tampered"));
        var denied = await connector.CompleteAuthAsync(new OAuthCallback(Code: string.Empty, State: firstChallenge.State!, Error: "access_denied"));
        var completed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: firstChallenge.State!));

        Assert.Equal("invalid-state", malformed.Error);
        Assert.Equal("consent-denied", denied.Error);
        Assert.True(completed.Success);
        Assert.Equal(1, handler.CallCount);

        var firstTokens = await store.GetAsync(UserScope(first), GoogleClientFactory.PackName);
        var secondTokens = await store.GetAsync(UserScope(second), GoogleClientFactory.PackName);
        Assert.True(firstTokens.ContainsKey(GoogleClientFactory.RefreshTokenKey));
        Assert.Empty(secondTokens);
    }

    [Fact]
    public async Task Salesforce_oauth_roundtrip_sends_pkce_and_keeps_tokens_principal_scoped()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, SalesforceAppConfig());
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"instance_url\":\"https://example.my.salesforce.com\",\"refresh_token\":\"refresh-a\",\"scope\":\"api refresh_token\"}");
        var connector = new SalesforceConnector(
            new FakeSalesforceApiClientFactory(),
            store,
            new FakeOAuthStateProtector(),
            tokenEndpointHandler: handler);
        var first = new NeuronId("principal-a");
        var second = new NeuronId("principal-b");

        var validation = await connector.ValidateConfigAsync(UserScope(first));
        var firstChallenge = await connector.BeginAuthAsync(first);
        var secondChallenge = await connector.BeginAuthAsync(second);

        Assert.True(validation.IsValid, validation.Message);
        Assert.False(firstChallenge.IsForm);
        Assert.False(secondChallenge.IsForm);
        Assert.NotEqual(firstChallenge.State, secondChallenge.State);
        Assert.DoesNotContain(first.Value, firstChallenge.State!, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Value, secondChallenge.State!, StringComparison.Ordinal);
        var firstPending = await store.GetAsync(
            UserScope(first),
            SalesforceClientFactory.OAuthPendingPackName);
        Assert.True(firstPending.ContainsKey(SalesforceClientFactory.OAuthPendingExpiresAtKey));

        var malformed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: "tampered"));
        var denied = await connector.CompleteAuthAsync(new OAuthCallback(Code: string.Empty, State: firstChallenge.State!, Error: "user_denied_authorization"));
        var replay = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: firstChallenge.State!));
        var freshChallenge = await connector.BeginAuthAsync(first);
        var completed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: freshChallenge.State!));

        Assert.Equal("invalid-state", malformed.Error);
        Assert.Equal("consent-denied", denied.Error);
        Assert.Equal("no-pending", replay.Error);
        Assert.True(completed.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("code_verifier=", handler.RequestBody, StringComparison.Ordinal);

        var firstTokens = await store.GetAsync(UserScope(first), SalesforceClientFactory.PackName);
        var secondTokens = await store.GetAsync(UserScope(second), SalesforceClientFactory.PackName);
        Assert.True(firstTokens.ContainsKey(SalesforceClientFactory.RefreshTokenKey));
        Assert.False(firstTokens.ContainsKey(SalesforceClientFactory.ClientIdKey));
        Assert.False(firstTokens.ContainsKey(SalesforceClientFactory.ClientSecretKey));
        Assert.False(firstTokens.ContainsKey(SalesforceClientFactory.RedirectUriKey));
        Assert.Empty(secondTokens);
    }

    [Fact]
    public async Task Salesforce_oauth_pins_login_and_redirect_for_the_started_flow()
    {
        var store = new FakePackConfigStore();
        var initial = new Dictionary<string, string>(SalesforceAppConfig())
        {
            [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com"
        };
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, initial);
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"instance_url\":\"https://example.my.salesforce.com\",\"refresh_token\":\"refresh-a\"}");
        var connector = new SalesforceConnector(
            new FakeSalesforceApiClientFactory(),
            store,
            new FakeOAuthStateProtector(),
            tokenEndpointHandler: handler);
        var owner = new NeuronId("principal-pinned-flow");
        var challenge = await connector.BeginAuthAsync(owner);
        var changed = new Dictionary<string, string>(initial)
        {
            [SalesforceClientFactory.LoginUrlKey] = "https://login.salesforce.com",
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:9090/oauth/callback/salesforce"
        };
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, changed);

        var result = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));

        Assert.True(result.Success);
        Assert.Equal("test.salesforce.com", handler.RequestUri?.Host);
        Assert.Contains(
            "redirect_uri=" + Uri.EscapeDataString(SalesforceClientFactory.DefaultRedirectUri),
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("9090", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Salesforce_app_owned_configuration_cannot_be_overridden_by_legacy_user_values()
    {
        var store = new FakePackConfigStore();
        var owner = new NeuronId("principal-legacy-config");
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, SalesforceAppConfig());
        await store.SetAsync(UserScope(owner), SalesforceClientFactory.PackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.ClientIdKey] = "stale-client-id",
            [SalesforceClientFactory.ClientSecretKey] = "stale-client-secret",
            [SalesforceClientFactory.LoginUrlKey] = "https://stale.my.salesforce.com",
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:9999/oauth/callback/salesforce",
            [SalesforceClientFactory.AccessTokenKey] = "user-access-token",
            [SalesforceClientFactory.InstanceUrlKey] = "https://example.my.salesforce.com"
        });

        var merged = await SalesforceClientFactory.GetMergedScopedValuesAsync(
            store,
            new NeuronScope(new UserId(owner.Value), ThreadId: null));

        Assert.Equal("client-id", merged[SalesforceClientFactory.ClientIdKey]);
        Assert.Equal("client-secret", merged[SalesforceClientFactory.ClientSecretKey]);
        Assert.Equal(SalesforceClientFactory.DefaultLoginUrl, merged[SalesforceClientFactory.LoginUrlKey]);
        Assert.Equal(SalesforceClientFactory.DefaultRedirectUri, merged[SalesforceClientFactory.RedirectUriKey]);
        Assert.Equal("user-access-token", merged[SalesforceClientFactory.AccessTokenKey]);
    }

    [Fact]
    public async Task Salesforce_scoped_validation_uses_app_config_and_purges_expired_pending_state()
    {
        var store = new FakePackConfigStore();
        var owner = new NeuronId("principal-expired-pending");
        var userScope = UserScope(owner);
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, SalesforceAppConfig());
        await store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.OAuthStateKey] = "expired-state",
            [SalesforceClientFactory.OAuthCodeVerifierKey] = "expired-verifier",
            [SalesforceClientFactory.OAuthPendingExpiresAtKey] = "0"
        });
        var connector = new SalesforceConnector(
            new FakeSalesforceApiClientFactory(),
            store,
            new FakeOAuthStateProtector());

        var validation = await connector.ValidateConfigAsync(userScope);
        var pending = await store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName);

        Assert.True(validation.IsValid, validation.Message);
        Assert.Empty(pending);
    }

    private static string UserScope(NeuronId owner) => PackConfigScopes.ForUser(new UserId(owner.Value));

    private static IReadOnlyDictionary<string, string> GoogleAppConfig() => new Dictionary<string, string>
    {
        [GoogleClientFactory.ClientIdKey] = "client-id",
        [GoogleClientFactory.ClientSecretKey] = "client-secret",
        [GoogleClientFactory.RedirectUriKey] = GoogleClientFactory.DefaultRedirectUri
    };

    private static IReadOnlyDictionary<string, string> SalesforceAppConfig() => new Dictionary<string, string>
    {
        [SalesforceClientFactory.ClientIdKey] = "client-id",
        [SalesforceClientFactory.ClientSecretKey] = "client-secret",
        [SalesforceClientFactory.RedirectUriKey] = SalesforceClientFactory.DefaultRedirectUri,
        [SalesforceClientFactory.LoginUrlKey] = SalesforceClientFactory.DefaultLoginUrl,
        [SalesforceClientFactory.ApiVersionKey] = SalesforceClientFactory.DefaultApiVersion
    };

    private sealed class StubTokenEndpointHandler(string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
