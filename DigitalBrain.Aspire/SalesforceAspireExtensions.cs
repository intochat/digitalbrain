using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

/// <summary>
/// Aspire wiring for Salesforce app-level configuration used by the kernel.
/// </summary>
public static class SalesforceAspireExtensions
{
    public const string ClientIdParameterName = "salesforce-client-id";
    public const string ClientSecretParameterName = "salesforce-client-secret";
    public const string LoginUrlParameterName = "salesforce-login-url";
    public const string ApiVersionParameterName = "salesforce-api-version";
    public const string DefaultCallbackPath = "/salesforce-callback";

    public const string DefaultLoginUrl = "https://login.salesforce.com";
    public const string DefaultApiVersion = "v60.0";

    public static SalesforceAppConfigParameters AddSalesforceAppConfig(
        this IDistributedApplicationBuilder builder)
    {
        var clientId = builder.AddParameter(ClientIdParameterName, secret: true)
            .WithDescription(
                "Salesforce Connected App client ID (consumer key). Required for Login via Salesforce.",
                enableMarkdown: true);

        var clientSecret = builder.AddParameter(ClientSecretParameterName, secret: true)
            .WithDescription(
                "Salesforce Connected App client secret. Required for OAuth token exchange.",
                enableMarkdown: true);

        var loginUrl = builder.AddParameter(LoginUrlParameterName, DefaultLoginUrl, publishValueAsDefault: true)
            .WithDescription(
                "Salesforce login URL. Use https://test.salesforce.com for sandboxes or a My Domain login URL when required.",
                enableMarkdown: true);

        var apiVersion = builder.AddParameter(ApiVersionParameterName, DefaultApiVersion, publishValueAsDefault: true)
            .WithDescription(
                "Salesforce REST API version used by CRM queries.",
                enableMarkdown: true);

        return new SalesforceAppConfigParameters(
            clientId,
            clientSecret,
            loginUrl,
            apiVersion,
            ResolveRedirectUri(builder));
    }

    public static IResourceBuilder<T> WithSalesforceAppConfig<T>(
        this IResourceBuilder<T> resource,
        SalesforceAppConfigParameters parameters)
        where T : IResourceWithEnvironment =>
        resource
            .WithEnvironment("DigitalBrain__Salesforce__ClientId", parameters.ClientId)
            .WithEnvironment("DigitalBrain__Salesforce__ClientSecret", parameters.ClientSecret)
            .WithEnvironment("DigitalBrain__Salesforce__LoginUrl", parameters.LoginUrl)
            .WithEnvironment("DigitalBrain__Salesforce__ApiVersion", parameters.ApiVersion)
            .WithEnvironment("DigitalBrain__Salesforce__RedirectUri", parameters.RedirectUri);

    private static string ResolveRedirectUri(IDistributedApplicationBuilder builder)
    {
        var configured = builder.Configuration["DigitalBrain:Salesforce:RedirectUri"]
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_SALESFORCE_REDIRECT_URI");

        return string.IsNullOrWhiteSpace(configured)
            ? $"http://localhost:{DigitalBrainBuilderExtensions.KernelWebPort(builder)}{DefaultCallbackPath}"
            : configured.Trim();
    }
}

public sealed record SalesforceAppConfigParameters(
    IResourceBuilder<ParameterResource> ClientId,
    IResourceBuilder<ParameterResource> ClientSecret,
    IResourceBuilder<ParameterResource> LoginUrl,
    IResourceBuilder<ParameterResource> ApiVersion,
    string RedirectUri);
