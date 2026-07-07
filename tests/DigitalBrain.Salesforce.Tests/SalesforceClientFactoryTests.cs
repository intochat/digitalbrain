using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceClientFactoryTests
{
    [Fact]
    public void TokenEndpoint_Appends_OAuth_Token_Path()
    {
        var endpoint = SalesforceClientFactory.TokenEndpoint("https://test.salesforce.com/");

        Assert.Equal("https://test.salesforce.com/services/oauth2/token", endpoint);
    }

    [Fact]
    public void TokenEndpoint_Normalizes_Host_Without_Scheme()
    {
        var endpoint = SalesforceClientFactory.TokenEndpoint("test.salesforce.com");

        Assert.Equal("https://test.salesforce.com/services/oauth2/token", endpoint);
    }

    [Fact]
    public void AuthorizationEndpoint_Appends_OAuth_Authorize_Path()
    {
        var endpoint = SalesforceClientFactory.AuthorizationEndpoint("https://test.salesforce.com/");

        Assert.Equal("https://test.salesforce.com/services/oauth2/authorize", endpoint);
    }

    [Fact]
    public void AuthorizationEndpoint_Normalizes_Host_Without_Scheme()
    {
        var endpoint = SalesforceClientFactory.AuthorizationEndpoint("test.salesforce.com");

        Assert.Equal("https://test.salesforce.com/services/oauth2/authorize", endpoint);
    }

    [Fact]
    public void CreateAuthorizationUrl_Uses_Web_Server_Flow()
    {
        var url = SalesforceClientFactory.CreateAuthorizationUrl(new Dictionary<string, string>
        {
            [SalesforceClientFactory.ClientIdKey] = "connected-app-id",
            [SalesforceClientFactory.ClientSecretKey] = "connected-app-secret",
            [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com"
        }, "http://localhost:8081/oauth/callback/salesforce", "state-1");

        Assert.StartsWith("https://test.salesforce.com/services/oauth2/authorize?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=connected-app-id", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A8081%2Foauth%2Fcallback%2Fsalesforce", url);
        Assert.Contains("scope=api%20refresh_token", url);
        Assert.DoesNotContain("offline_access", url);
        Assert.Contains("state=state-1", url);
    }

    [Fact]
    public void CreateAuthorizationUrl_Includes_PKCE_When_Code_Challenge_Is_Provided()
    {
        var url = SalesforceClientFactory.CreateAuthorizationUrl(new Dictionary<string, string>
        {
            [SalesforceClientFactory.ClientIdKey] = "connected-app-id",
            [SalesforceClientFactory.ClientSecretKey] = "connected-app-secret",
            [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com"
        }, "http://localhost:8081/oauth/callback/salesforce", "state-1", "challenge-1");

        Assert.Contains("code_challenge=challenge-1", url);
        Assert.Contains("code_challenge_method=S256", url);
    }

    [Fact]
    public void CreatePkceCodeChallenge_Uses_Rfc7636_S256_Example()
    {
        var challenge = SalesforceClientFactory.CreatePkceCodeChallenge(
            "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }

    [Fact]
    public void CreatePkceCodeVerifier_Produces_Valid_Verifier()
    {
        var verifier = SalesforceClientFactory.CreatePkceCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
    }

    [Fact]
    public void HasUsableCredential_Accepts_OAuth_Tokens()
    {
        Assert.True(SalesforceClientFactory.HasUsableCredential(new Dictionary<string, string>
        {
            [SalesforceClientFactory.AccessTokenKey] = "access-token",
            [SalesforceClientFactory.InstanceUrlKey] = "https://example.my.salesforce.com"
        }));
    }

    [Fact]
    public async Task CreateForceClientAsync_Uses_Access_Token_When_Refresh_Token_Cannot_Be_Refreshed()
    {
        var client = await SalesforceClientFactory.CreateForceClientAsync(new Dictionary<string, string>
        {
            [SalesforceClientFactory.AccessTokenKey] = "access-token",
            [SalesforceClientFactory.RefreshTokenKey] = "refresh-token",
            [SalesforceClientFactory.InstanceUrlKey] = "https://example.my.salesforce.com"
        });

        Assert.NotNull(client);
    }

    [Fact]
    public async Task CreateForceClientAsync_Missing_Config_Throws_Clear_Error()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SalesforceClientFactory.CreateForceClientAsync(new Dictionary<string, string>()));

        Assert.Contains("missing client_id", ex.Message);
        Assert.Contains("Salesforce", ex.Message);
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_Uses_Provided_Handler_Instead_Of_Real_Network_Call()
    {
        var handler = new FakeSalesforceTokenHandler("fake-access-token", "https://fake.my.salesforce.com", "fake-refresh-token");

        var result = await SalesforceClientFactory.ExchangeAuthorizationCodeAsync(
            new Dictionary<string, string>
            {
                [SalesforceClientFactory.ClientIdKey] = "connected-app-id",
                [SalesforceClientFactory.ClientSecretKey] = "connected-app-secret",
                [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com",
                [SalesforceClientFactory.OAuthCodeVerifierKey] = "verifier-1"
            },
            "auth-code-1",
            "http://localhost:8081/oauth/callback/salesforce",
            handler);

        Assert.Equal("fake-access-token", result[SalesforceClientFactory.AccessTokenKey]);
        Assert.Equal("https://fake.my.salesforce.com", result[SalesforceClientFactory.InstanceUrlKey]);
        Assert.Equal("fake-refresh-token", result[SalesforceClientFactory.RefreshTokenKey]);
        Assert.Equal(1, handler.RequestCount);
    }
}
