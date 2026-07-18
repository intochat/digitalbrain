using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.AppHost;

internal static class GoogleAspireExtensions
{
    public const string ClientIdParameterName = "google-client-id";
    public const string ClientSecretParameterName = "google-client-secret";
    public const string RedirectUriParameterName = "google-redirect-uri";
    public const string DefaultCallbackPath = "/oauth/callback/google";
    private const string GoogleCloudCredentialsUrl = "https://console.cloud.google.com/apis/credentials";
    private const string GoogleCloudAudienceUrl = "https://console.cloud.google.com/auth/audience";
    private const string GoogleOAuthWebServerGuideUrl = "https://developers.google.com/identity/protocols/oauth2/web-server";
    private const string GoogleOAuthVerificationHelpUrl = "https://support.google.com/cloud/answer/15549945";
    public static GoogleAppConfigParameters AddGoogleAppConfig(this IDistributedApplicationBuilder builder)
    {
        var defaultRedirectUri = ResolveRedirectUri(builder);
        var clientId = builder.AddParameter(ClientIdParameterName, secret: true).WithDescription(GoogleOAuthParameterDescription("OAuth client ID", defaultRedirectUri), enableMarkdown: true);
        var clientSecret = builder.AddParameter(ClientSecretParameterName, secret: true).WithDescription(GoogleOAuthParameterDescription("OAuth client secret", defaultRedirectUri), enableMarkdown: true);
        var redirectUri = builder.AddParameter(RedirectUriParameterName, defaultRedirectUri, publishValueAsDefault: true).WithDescription(GoogleRedirectUriDescription(defaultRedirectUri), enableMarkdown: true);
        return new GoogleAppConfigParameters(clientId, clientSecret, redirectUri);
    }
    public static IResourceBuilder<T> WithGoogleAppConfig<T>(this IResourceBuilder<T> resource, GoogleAppConfigParameters parameters)
        where T : IResourceWithEnvironment =>
        resource.WithEnvironment("DigitalBrain__Google__ClientId", parameters.ClientId).WithEnvironment("DigitalBrain__Google__ClientSecret", parameters.ClientSecret)
            .WithEnvironment("DigitalBrain__Google__RedirectUri", parameters.RedirectUri);
    private static string ResolveRedirectUri(IDistributedApplicationBuilder builder)
    {
        var configured = builder.Configuration["DigitalBrain:Google:RedirectUri"] ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_GOOGLE_REDIRECT_URI");
        return string.IsNullOrWhiteSpace(configured)
            ? $"http://localhost:{DigitalBrainBuilderExtensions.KernelWebPort(builder)}{DefaultCallbackPath}"
            : configured.Trim();
    }
    private static string GoogleOAuthParameterDescription(string valueName, string redirectUri) =>
        $"Configure the OAuth consent screen, then go to [Google Cloud credentials]({GoogleCloudCredentialsUrl}) and choose **Create credentials > OAuth client ID**. " +
        $"Select **Web application**. Do **not** create a Service account; this integration signs in a Google user through OAuth consent and needs a client ID, client secret, and redirect URI. " +
        $"If Google shows `Access blocked: DigitalBrain has not completed the Google verification process`, open [Google Auth audience]({GoogleCloudAudienceUrl}) and add the signing-in account under **Test users**, or publish the app and complete verification before using non-tester accounts. " +
        $"Add `{redirectUri}` as an authorized redirect URI for local Aspire runs, and register the exact production callback from `{RedirectUriParameterName}` for cloud deployments. Paste the {valueName}. " +
        $"See the [Google OAuth web-server guide]({GoogleOAuthWebServerGuideUrl}).";
    private static string GoogleRedirectUriDescription(string defaultRedirectUri) =>
        $"OAuth callback URI sent to Google. Local Aspire default: `{defaultRedirectUri}`. " +
        $"For production, set this parameter to the public HTTPS callback, for example `https://brain.example.com{DefaultCallbackPath}`, and register that exact URI in Google Cloud. " +
        $"Testing-mode apps can only be authorized by users listed under [Google Auth audience]({GoogleCloudAudienceUrl}); tester authorizations and refresh tokens expire after seven days. " +
        $"Use a separate OAuth client per environment when possible so local, staging, and production credentials do not share tokens. See [Google app audience rules]({GoogleOAuthVerificationHelpUrl}).";
}
internal sealed record GoogleAppConfigParameters(IResourceBuilder<ParameterResource> ClientId, IResourceBuilder<ParameterResource> ClientSecret, IResourceBuilder<ParameterResource> RedirectUri);
