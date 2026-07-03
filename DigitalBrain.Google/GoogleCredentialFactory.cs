using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

namespace DigitalBrain.Google;

public static class GoogleCredentialFactory
{
    public static UserCredential FromRefreshToken(string clientId, string clientSecret, string refreshToken, params string[] scopes)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = scopes
        });
        var token = new TokenResponse { RefreshToken = refreshToken };
        return new UserCredential(flow, "digitalbrain-user", token);
    }

    /// <summary>
    /// Generates the Google OAuth consent URL for the given client and scopes.
    /// Client is responsible for launching the URL and handling the redirect/callback.
    /// </summary>
    public static string CreateAuthorizationUrl(string clientId, string clientSecret, string redirectUri, params string[] scopes)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = scopes
        });

        var codeRequest = flow.CreateAuthorizationCodeRequest(redirectUri);
        return codeRequest.Build().AbsoluteUri;
    }
}
