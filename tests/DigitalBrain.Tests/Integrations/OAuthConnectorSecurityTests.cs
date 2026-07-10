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

        var firstChallenge = await connector.BeginAuthAsync(first);
        var secondChallenge = await connector.BeginAuthAsync(second);

        Assert.False(firstChallenge.IsForm);
        Assert.False(secondChallenge.IsForm);
        Assert.NotEqual(firstChallenge.State, secondChallenge.State);
        Assert.DoesNotContain(first.Value, firstChallenge.State!, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Value, secondChallenge.State!, StringComparison.Ordinal);

        var malformed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: "tampered"));
        var denied = await connector.CompleteAuthAsync(new OAuthCallback(Code: string.Empty, State: firstChallenge.State!, Error: "user_denied_authorization"));
        var completed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: firstChallenge.State!));

        Assert.Equal("invalid-state", malformed.Error);
        Assert.Equal("consent-denied", denied.Error);
        Assert.True(completed.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("code_verifier=", handler.RequestBody, StringComparison.Ordinal);

        var firstTokens = await store.GetAsync(UserScope(first), SalesforceClientFactory.PackName);
        var secondTokens = await store.GetAsync(UserScope(second), SalesforceClientFactory.PackName);
        Assert.True(firstTokens.ContainsKey(SalesforceClientFactory.RefreshTokenKey));
        Assert.Empty(secondTokens);
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
        [SalesforceClientFactory.ApiVersionKey] = SalesforceClientFactory.DefaultApiVersion,
        [SalesforceClientFactory.OAuthScopeKey] = SalesforceClientFactory.DefaultOAuthScope
    };

    private sealed class StubTokenEndpointHandler(string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
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
