using DigitalBrain.Core.Config;
using Salesforce.Common;
using Salesforce.Force;

namespace DigitalBrain.Salesforce;

public static class SalesforceClientFactory
{
    public const string PackName = "salesforce";
    public const string DefaultScope = "default";
    public const string DefaultLoginUrl = "https://login.salesforce.com";
    public const string DefaultApiVersion = "v36.0";

    public const string ClientIdKey = "client_id";
    public const string ClientSecretKey = "client_secret";
    public const string UsernameKey = "username";
    public const string PasswordKey = "password";
    public const string SecurityTokenKey = "security_token";
    public const string LoginUrlKey = "login_url";
    public const string ApiVersionKey = "api_version";

    public static async Task<SalesforceApiClient> CreateApiClientAsync(
        IPackConfigStore store,
        string scope = DefaultScope)
    {
        var values = await store.GetAsync(scope, PackName).ConfigureAwait(false);
        return new SalesforceApiClient(await CreateForceClientAsync(values).ConfigureAwait(false));
    }

    public static async Task<ForceClient> CreateForceClientAsync(IReadOnlyDictionary<string, string> values)
    {
        var clientId = Required(values, ClientIdKey);
        var clientSecret = Required(values, ClientSecretKey);
        var username = Required(values, UsernameKey);
        var password = Required(values, PasswordKey);
        var passwordWithToken = password + Optional(values, SecurityTokenKey);
        var loginUrl = Optional(values, LoginUrlKey, DefaultLoginUrl);
        var apiVersion = NormalizeApiVersion(Optional(values, ApiVersionKey, DefaultApiVersion));

        using var auth = new AuthenticationClient(apiVersion);
        await auth.UsernamePasswordAsync(
            clientId,
            clientSecret,
            username,
            passwordWithToken,
            TokenEndpoint(loginUrl)).ConfigureAwait(false);

        return new ForceClient(auth.InstanceUrl, auth.AccessToken, auth.ApiVersion);
    }

    public static string TokenEndpoint(string loginUrlOrEndpoint)
    {
        var value = string.IsNullOrWhiteSpace(loginUrlOrEndpoint)
            ? DefaultLoginUrl
            : loginUrlOrEndpoint.Trim();

        if (value.EndsWith("/services/oauth2/token", StringComparison.OrdinalIgnoreCase))
            return value;

        return value.TrimEnd('/') + "/services/oauth2/token";
    }

    private static string NormalizeApiVersion(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('v') ? trimmed : "v" + trimmed;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException(
            $"Salesforce pack config (scope '{DefaultScope}', pack '{PackName}') is missing {key}. " +
            "Complete the Salesforce credentials prompt before using Salesforce CRM neurons.");
    }

    private static string Optional(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback = "") =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
}
