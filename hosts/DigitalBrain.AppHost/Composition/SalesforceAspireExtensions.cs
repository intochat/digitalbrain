using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.AppHost;

internal static class SalesforceAspireExtensions
{
    public const string ClientIdParameterName = "salesforce-client-id";
    public const string ClientSecretParameterName = "salesforce-client-secret";
    public const string RedirectUriParameterName = "salesforce-redirect-uri";
    public const string LoginUrlParameterName = "salesforce-login-url";
    public const string ApiVersionParameterName = "salesforce-api-version";
    public const string DefaultCallbackPath = OAuthCallbackPaths.Salesforce;

    public const string DefaultLoginUrl = "https://login.salesforce.com";
    public const string DefaultApiVersion = "v60.0";

    private const string SalesforceSetupUrl = "https://login.salesforce.com/lightning/setup/SetupOneHome/home";
    private const string SalesforceConnectedAppGuideUrl = "https://developer.salesforce.com/docs/platform/mobile-sdk/guide/connected-apps-howto";

    public static SalesforceAppConfigParameters AddSalesforceAppConfig(this IDistributedApplicationBuilder builder)
    {
        var defaultRedirectUri = ResolveRedirectUri(builder);

        var clientId = builder.AddParameter(ClientIdParameterName, secret: true).WithDescription(SalesforceConnectedAppParameterDescription("Consumer Key (client ID)", defaultRedirectUri), enableMarkdown: true);

        var clientSecret = builder.AddParameter(ClientSecretParameterName, secret: true).WithDescription(SalesforceConnectedAppParameterDescription("Consumer Secret (client secret)", defaultRedirectUri), enableMarkdown: true);

        var redirectUri = builder.AddParameter(RedirectUriParameterName, defaultRedirectUri, publishValueAsDefault: true).WithDescription(SalesforceRedirectUriDescription(defaultRedirectUri), enableMarkdown: true);

        var loginUrl = builder.AddParameter(LoginUrlParameterName, DefaultLoginUrl, publishValueAsDefault: true).WithDescription(
                "Salesforce login URL. Use [login.salesforce.com](https://login.salesforce.com) for production, [test.salesforce.com](https://test.salesforce.com) for sandboxes, or your My Domain login URL.",
                enableMarkdown: true);

        var apiVersion = builder.AddParameter(ApiVersionParameterName, DefaultApiVersion, publishValueAsDefault: true).WithDescription("Salesforce REST API version used by CRM queries, for example `v60.0`.", enableMarkdown: true);

        return new SalesforceAppConfigParameters(clientId, clientSecret, redirectUri, loginUrl, apiVersion, defaultRedirectUri);
    }

    public static IResourceBuilder<T> WithSalesforceAppConfig<T>(this IResourceBuilder<T> resource, SalesforceAppConfigParameters parameters)
        where T : IResourceWithEnvironment =>
        resource.WithEnvironment("DigitalBrain__Salesforce__ClientId", parameters.ClientId).WithEnvironment("DigitalBrain__Salesforce__ClientSecret", parameters.ClientSecret)
            .WithEnvironment("DigitalBrain__Salesforce__LoginUrl", parameters.LoginUrl)
            .WithEnvironment("DigitalBrain__Salesforce__ApiVersion", parameters.ApiVersion)
            .WithEnvironment("DigitalBrain__Salesforce__RedirectUri", parameters.RedirectUri);

    private static string ResolveRedirectUri(IDistributedApplicationBuilder builder)
    {
        var configured = builder.Configuration[$"Parameters:{RedirectUriParameterName}"]
            ?? builder.Configuration["DigitalBrain:Salesforce:RedirectUri"]
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_SALESFORCE_REDIRECT_URI");

        return string.IsNullOrWhiteSpace(configured)
            ? $"http://localhost:{DigitalBrainBuilderExtensions.KernelWebPort(builder)}{DefaultCallbackPath}"
            : configured.Trim();
    }

    private static string SalesforceConnectedAppParameterDescription(string valueName, string redirectUri) =>
        $"Create a Connected App from [Salesforce Setup]({SalesforceSetupUrl}) (Setup > Apps > External Client Apps). " +
        $"Enable OAuth settings, add `{redirectUri}` as the callback URL for local Aspire runs, include `api` and `refresh_token` scopes, and register the exact production callback from `{RedirectUriParameterName}` for cloud deployments. Paste the {valueName}. " +
        $"See the [Salesforce Connected App guide]({SalesforceConnectedAppGuideUrl}).";

    private static string SalesforceRedirectUriDescription(string defaultRedirectUri) =>
        $"OAuth callback URI sent to Salesforce. Local Aspire default: `{defaultRedirectUri}`. " +
        $"For production, set this parameter to the public HTTPS callback, for example `https://brain.example.com{DefaultCallbackPath}`, and configure the exact same callback URL in the Connected App. " +
        "Use a separate Connected App per environment when possible so local, sandbox, staging, and production credentials stay isolated.";
}

internal sealed record SalesforceAppConfigParameters(
    IResourceBuilder<ParameterResource> ClientId,
    IResourceBuilder<ParameterResource> ClientSecret,
    IResourceBuilder<ParameterResource> RedirectUri,
    IResourceBuilder<ParameterResource> LoginUrl,
    IResourceBuilder<ParameterResource> ApiVersion,
    string RedirectUriValue);
