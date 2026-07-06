using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire;

public static class GoogleAspireExtensions
{
    public const string ClientIdParameterName = "google-client-id";
    public const string ClientSecretParameterName = "google-client-secret";
    public const string DefaultCallbackPath = "/google-callback";

    public static GoogleAppConfigParameters AddGoogleAppConfig(
        this IDistributedApplicationBuilder builder)
    {
        var clientId = builder.AddParameter(ClientIdParameterName, secret: true);

        var clientSecret = builder.AddParameter(ClientSecretParameterName, secret: true);

        return new GoogleAppConfigParameters(
            clientId,
            clientSecret,
            ResolveRedirectUri(builder));
    }

    public static IResourceBuilder<T> WithGoogleAppConfig<T>(
        this IResourceBuilder<T> resource,
        GoogleAppConfigParameters parameters)
        where T : IResourceWithEnvironment =>
        resource
            .WithEnvironment("DigitalBrain__Google__ClientId", parameters.ClientId)
            .WithEnvironment("DigitalBrain__Google__ClientSecret", parameters.ClientSecret)
            .WithEnvironment("DigitalBrain__Google__RedirectUri", parameters.RedirectUri);

    private static string ResolveRedirectUri(IDistributedApplicationBuilder builder)
    {
        var configured = builder.Configuration["DigitalBrain:Google:RedirectUri"]
            ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_GOOGLE_REDIRECT_URI");

        return string.IsNullOrWhiteSpace(configured)
            ? $"http://localhost:{DigitalBrainBuilderExtensions.KernelWebPort(builder)}{DefaultCallbackPath}"
            : configured.Trim();
    }
}

public sealed record GoogleAppConfigParameters(
    IResourceBuilder<ParameterResource> ClientId,
    IResourceBuilder<ParameterResource> ClientSecret,
    string RedirectUri);