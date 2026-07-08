using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Abstractions;

/// Shared integration contract (P2 start). One base eliminates bespoke per-provider forks (G-I1/G-I7).
/// Google, Salesforce, Telegram migrate to this for per-user, PKCE, unified callback, contract tests.
public interface IConnector
{
    ConnectorDescriptor Descriptor { get; }

    Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null, CancellationToken cancellationToken = default);

    Task<AuthChallenge> BeginAuthAsync(NeuronId user, string? clientIdHint = null, CancellationToken cancellationToken = default);

    Task<AuthResult> CompleteAuthAsync(OAuthCallback callback, CancellationToken cancellationToken = default);

    /// Connection health (G-I3): cheap probe (labels.list for gmail, etc). Used for Aspire health + UI status.
    Task<ConnectionHealth> TestConnectionAsync(NeuronId user, CancellationToken cancellationToken = default);
}

[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Abstractions.ConnectorDescriptor")]
public record ConnectorDescriptor(
    string Id,
    string DisplayName,
    IReadOnlyList<string> RequiredConfigKeys,
    IReadOnlyList<string> Scopes);

[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Abstractions.ConnectorConfigStatus")]
public record ConnectorConfigStatus(bool IsValid, string? MissingKey = null, string? Message = null);

[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Abstractions.AuthChallenge")]
public record AuthChallenge(string UrlOrForm, bool IsForm = false, string? State = null);

[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Abstractions.AuthResult")]
public record AuthResult(bool Success, string? Error = null, string? Details = null);

[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Abstractions.ConnectionHealth")]
public record ConnectionHealth(bool Healthy, string? Detail = null, DateTimeOffset Checked = default);

[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Abstractions.OAuthCallback")]
public record OAuthCallback(string Code, string State, string? Error = null, string? ErrorDescription = null, string? FallbackRedirectUri = null);
