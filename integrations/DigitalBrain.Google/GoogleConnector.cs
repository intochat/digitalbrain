using DigitalBrain.Core;
using DigitalBrain.Kernel.Abstractions;

namespace DigitalBrain.Google;

/// Initial IConnector implementation for Google (P2 Phase 1).
/// Stub for auth; health uses basic (real will use IGmailApiClient or similar for labels.list probe).
public class GoogleConnector : IConnector
{
    public ConnectorDescriptor Descriptor => new(
        Id: "google",
        DisplayName: "Google",
        RequiredConfigKeys: new[] { "clientId", "clientSecret", "redirectUri" },
        Scopes: new[] { "https://www.googleapis.com/auth/gmail.readonly" });

    public Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null)
    {
        return Task.FromResult(new ConnectorConfigStatus(IsValid: true));
    }

    public Task<AuthChallenge> BeginAuthAsync(NeuronId user, string? clientIdHint = null)
    {
        return Task.FromResult(new AuthChallenge(UrlOrForm: "https://accounts.google.com/o/oauth2/v2/auth (use form)", IsForm: true));
    }

    public Task<AuthResult> CompleteAuthAsync(OAuthCallback callback)
    {
        return Task.FromResult(new AuthResult(Success: true));
    }

    public Task<ConnectionHealth> TestConnectionAsync(NeuronId user)
    {
        // Stub; in full migration, create client and call a cheap API like labels.list
        return Task.FromResult(new ConnectionHealth(Healthy: true, Detail: "Google connector health stub", Checked: DateTimeOffset.UtcNow));
    }
}