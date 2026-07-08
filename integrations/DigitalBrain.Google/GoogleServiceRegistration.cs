using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using Microsoft.Extensions.DependencyInjection;
using GoogleApisAuth = global::Google.Apis.Auth;
using GoogleApisGmail = global::Google.Apis.Gmail;

namespace DigitalBrain.Google;

public static class GoogleServiceRegistration
{
    // Deprecated: eager client registration for global "gmail-main" no longer used.
    // Per-user clients are now created via IGmailApiClientFactory + GmailNeuron using Self.AsScope().
    // Kept for reference / possible test compat; call removed from Program.cs.
    public static IServiceCollection AddGoogleGmailClient(this IServiceCollection services)
    {
        // No-op or legacy path. Use GmailApiClientFactory instead.
        return services;
    }
}
