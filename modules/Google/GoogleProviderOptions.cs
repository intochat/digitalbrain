using Microsoft.Extensions.Configuration;

namespace Brain.Modules.Google;

public sealed record GoogleProviderOptions(string ClientId, string ClientSecret, string RedirectUri)
{
    public static GoogleProviderOptions? FromConfiguration(IConfiguration config)
    {
        var clientId = config["Brain:Connections:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var clientSecret = config["Brain:Connections:Google:ClientSecret"] ?? "";
        var redirectUri = config["Brain:Connections:Google:RedirectUri"] ?? "http://localhost:5320/oauth/callback/google";
        return new GoogleProviderOptions(clientId, clientSecret, redirectUri);
    }
}
