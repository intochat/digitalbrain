using Google.Apis.Auth.OAuth2.Flows;

namespace DigitalBrain.Google.Auth;

internal sealed class GoogleOAuthFlowInitializer : GoogleAuthorizationCodeFlow.Initializer
{
    public GoogleOAuthFlowInitializer()
    {
    }

    public GoogleOAuthFlowInitializer(string authorizationServerUrl, string tokenServerUrl, string revokeTokenUrl)
        : base(authorizationServerUrl, tokenServerUrl, revokeTokenUrl)
    {
    }
}
