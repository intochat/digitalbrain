using Microsoft.Extensions.Configuration;

namespace Brain.Modules.Salesforce;

public sealed record SalesforceProviderOptions(string ClientId, string ClientSecret, string RedirectUri, string LoginHost, string ApiVersion)
{
    public static SalesforceProviderOptions? FromConfiguration(IConfiguration config)
    {
        var clientId = config["Brain:Connections:Salesforce:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var clientSecret = config["Brain:Connections:Salesforce:ClientSecret"] ?? "";
        var redirectUri = config["Brain:Connections:Salesforce:RedirectUri"] ?? "http://localhost:5320/oauth/callback/salesforce";
        var loginHost = config["Brain:Connections:Salesforce:LoginHost"] ?? "https://login.salesforce.com";
        var apiVersion = config["Brain:Connections:Salesforce:ApiVersion"] ?? "v60.0";
        return new SalesforceProviderOptions(clientId, clientSecret, redirectUri, loginHost, apiVersion);
    }
}
