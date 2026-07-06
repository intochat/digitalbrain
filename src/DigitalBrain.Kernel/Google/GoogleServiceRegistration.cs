using DigitalBrain.Core.Config;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Google;

using GoogleApisAuth = global::Google.Apis.Auth;
using GoogleApisGmail = global::Google.Apis.Gmail;

namespace DigitalBrain.Kernel.Google;

internal static class GoogleServiceRegistration
{
    public static IServiceCollection AddGoogleGmailClient(this IServiceCollection services)
    {
        services.AddScoped(sp => BuildGoogleCredential(sp, GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName));
        services.AddScoped<DigitalBrain.Google.IGmailApiClient>(sp =>
            new DigitalBrain.Google.GoogleGmailApiClient(sp.GetRequiredService<GoogleApisAuth.OAuth2.UserCredential>()));

        return services;
    }

    // Reads client_id/client_secret/refresh_token from pack config (default scope + "google" pack) and builds UserCredential.
    // Uses GoogleClientFactory keys for consistency. Throws early if no refresh token (user must complete sign-in).
    private static GoogleApisAuth.OAuth2.UserCredential BuildGoogleCredential(IServiceProvider sp, string scope, string pack)
    {
        var store = sp.GetRequiredService<IPackConfigStore>();
        var values = store.GetAsync(scope, pack).GetAwaiter().GetResult();

        if (!values.TryGetValue(GoogleClientFactory.ClientIdKey, out var clientId) ||
            !values.TryGetValue(GoogleClientFactory.ClientSecretKey, out var clientSecret) ||
            !values.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var refreshToken))
        {
            throw new InvalidOperationException(
                $"Google pack config (scope '{scope}', pack '{pack}') is missing client_id/client_secret/refresh_token. " +
                "Complete \"Sign in with Google\" before using Gmail neurons.");
        }

        return DigitalBrain.Google.GoogleCredentialFactory.FromRefreshToken(
            clientId, clientSecret, refreshToken,
            GoogleApisGmail.v1.GmailService.ScopeConstants.MailGoogleCom);
    }
}
