using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IntoChat.Agent;

// D14: capture content only for the local owner's development runs. The host must be
// Development, the principal the open/default owner, and the profile absent or explicitly
// developer; hosted deployments, other principals, product and unknown profiles all stay off,
// and an unknown identity or class also defaults off. The deployment-level dev signal is the
// explicit DigitalBrain:AI:Telemetry:EnableSensitiveData setting the AppHost forwards; this
// policy adds the host-environment, identity and profile gates. The minimal class tag travels
// with the request message; the semantic type catalog replaces it in Phase 1.
internal static class ContentCapturePolicy
{
    internal const string ProfileKey = "IntoChat:Profile";
    internal const string DeveloperProfile = "developer";

    public static bool IsLocalOwner(BasicAuthOptions auth, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var development = environment.IsDevelopment();
        var owner = string.IsNullOrEmpty(auth.Username)
            || string.Equals(auth.Username, BasicAuthGate.DefaultLogin, StringComparison.Ordinal);
        var profile = configuration[ProfileKey];
        var developerProfile = string.IsNullOrEmpty(profile)
            || string.Equals(profile, DeveloperProfile, StringComparison.OrdinalIgnoreCase);
        return development && owner && developerProfile;
    }

    public static ContentClass ClassOf(IReadOnlyList<AgentEndpoints.AgentMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var result = ContentClass.Ordinary;
        foreach (var message in messages)
        {
            var @class = Parse(message.Class);
            if (@class == ContentClass.Unknown) { return ContentClass.Unknown; }
            if (@class > result) { result = @class; }
        }

        return result;
    }

    private static ContentClass Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "ordinary" => ContentClass.Ordinary,
        "personal" => ContentClass.Personal,
        "credential" or "secret" => ContentClass.Credential,
        _ => ContentClass.Unknown,
    };
}