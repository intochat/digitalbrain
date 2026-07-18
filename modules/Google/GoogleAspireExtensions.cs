using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Brain.Modules.Google;

public static class GoogleAspireExtensions
{
    public static IResourceBuilder<T> WithDigitalBrainGoogle<T>(
        this IResourceBuilder<T> kernel)
        where T : IResourceWithEnvironment
    {
        var builder = kernel.ApplicationBuilder;
        var clientId = builder.AddParameter("google-client-id", secret: true);
        var clientSecret = builder.AddParameter("google-client-secret", secret: true);
        var redirectUri = builder.AddParameter(
            "google-redirect-uri",
            "http://localhost:5311/oauth/callback/google",
            publishValueAsDefault: true);

        return kernel
            .WithEnvironment("DigitalBrain__Google__ClientId", clientId)
            .WithEnvironment("DigitalBrain__Google__ClientSecret", clientSecret)
            .WithEnvironment("DigitalBrain__Google__RedirectUri", redirectUri);
    }
}
