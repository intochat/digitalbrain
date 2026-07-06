using DigitalBrain.Core.Config;
using Microsoft.Extensions.DependencyInjection;

using GoogleApisAuth = global::Google.Apis.Auth;
using GoogleApisGmail = global::Google.Apis.Gmail;
using GoogleApisDrive = global::Google.Apis.Drive;
using GoogleApisCalendar = global::Google.Apis.Calendar;

namespace DigitalBrain.Kernel.Google;

internal static class GoogleServiceRegistration
{
    public static IServiceCollection AddGoogleWorkspaceClients(this IServiceCollection services)
    {
        services.AddScoped(sp => BuildGoogleCredential(sp, "google", "default"));
        services.AddScoped<DigitalBrain.Google.IGmailApiClient>(sp =>
            new DigitalBrain.Google.GoogleGmailApiClient(sp.GetRequiredService<GoogleApisAuth.OAuth2.UserCredential>()));
        services.AddScoped<DigitalBrain.Google.IGoogleDriveApiClient>(sp =>
            new DigitalBrain.Google.GoogleDriveApiClient(sp.GetRequiredService<GoogleApisAuth.OAuth2.UserCredential>()));
        services.AddScoped<DigitalBrain.Google.IGoogleCalendarApiClient>(sp =>
            new DigitalBrain.Google.GoogleCalendarApiClient(sp.GetRequiredService<GoogleApisAuth.OAuth2.UserCredential>()));

        return services;
    }

    // Reads client_id/client_secret/refresh_token from the given pack-config scope/pack and builds a UserCredential.
    // Config not yet provided throws so grain activation fails fast and loudly rather than constructing a client
    // that will 401 on first real call.
    private static GoogleApisAuth.OAuth2.UserCredential BuildGoogleCredential(IServiceProvider sp, string pack, string scope)
    {
        var store = sp.GetRequiredService<IPackConfigStore>();
        var values = store.GetAsync(scope, pack).GetAwaiter().GetResult();

        if (!values.TryGetValue("client_id", out var clientId) ||
            !values.TryGetValue("client_secret", out var clientSecret) ||
            !values.TryGetValue("refresh_token", out var refreshToken))
        {
            throw new InvalidOperationException(
                $"Google pack config (scope '{scope}', pack '{pack}') is missing client_id/client_secret/refresh_token. " +
                "Complete \"Sign in with Google\" before using Gmail/Drive/Calendar neurons.");
        }

        return DigitalBrain.Google.GoogleCredentialFactory.FromRefreshToken(
            clientId, clientSecret, refreshToken,
            GoogleApisGmail.v1.GmailService.ScopeConstants.MailGoogleCom,
            GoogleApisDrive.v3.DriveService.ScopeConstants.Drive,
            GoogleApisCalendar.v3.CalendarService.ScopeConstants.Calendar);
    }
}