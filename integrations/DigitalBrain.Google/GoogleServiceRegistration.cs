using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Google;

using GoogleApisAuth = global::Google.Apis.Auth;
using GoogleApisGmail = global::Google.Apis.Gmail;

namespace DigitalBrain.Google;

public static class GoogleServiceRegistration
{
    public static IServiceCollection AddGoogleGmailClient(this IServiceCollection services)
    {
        services.AddScoped(sp => BuildGoogleCredential(sp, GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName));
        services.AddScoped<DigitalBrain.Google.IGmailApiClient>(sp =>
            new DigitalBrain.Google.GoogleGmailApiClient(sp.GetRequiredService<GoogleApisAuth.OAuth2.UserCredential>()));
        return services;
    }

    private static GoogleApisAuth.OAuth2.UserCredential BuildGoogleCredential(IServiceProvider sp, string scope, string pack)
    {
        var store = sp.GetRequiredService<IPackConfigStore>();
        // Use merged (default + user) so tokens written under user:{id} are visible (fixes scope mismatch).
        var values = GoogleClientFactory.GetMergedScopedValuesAsync(store, new NeuronScope(UserId.Anonymous, null)).GetAwaiter().GetResult();

        if (!values.TryGetValue(GoogleClientFactory.ClientIdKey, out var clientId) ||
            !values.TryGetValue(GoogleClientFactory.ClientSecretKey, out var clientSecret) ||
            !values.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var refreshToken))
        {
            throw new InvalidOperationException($"Google pack config (scope '{scope}', pack '{pack}') is missing keys. Complete sign in.");
        }

        return DigitalBrain.Google.GoogleCredentialFactory.FromRefreshToken(clientId, clientSecret, refreshToken, GoogleApisGmail.v1.GmailService.ScopeConstants.MailGoogleCom);
    }
}
