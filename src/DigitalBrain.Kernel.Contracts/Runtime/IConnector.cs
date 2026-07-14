using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Kernel.Contracts;

public interface IConnector
{
    ConnectorDescriptor Descriptor { get; }
    Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null, CancellationToken cancellationToken = default);
    Task<AuthChallenge> BeginAuthAsync(NeuronId user, string? clientIdHint = null, CancellationToken cancellationToken = default);
    Task<AuthResult> CompleteAuthAsync(OAuthCallback callback, CancellationToken cancellationToken = default);
    Task<ConnectionHealth> TestConnectionAsync(NeuronId user, CancellationToken cancellationToken = default);
}
public interface IOAuthStateProtector
{
    string Protect(NeuronId owner);
    bool TryUnprotect(string state, out NeuronId owner);
}
[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Contracts.ConnectorDescriptor")]
public record ConnectorDescriptor(string Id, string DisplayName, IReadOnlyList<string> RequiredConfigKeys, IReadOnlyList<string> Scopes);
[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Contracts.ConnectorConfigStatus")]
public record ConnectorConfigStatus(bool IsValid, string? MissingKey = null, string? Message = null);
[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Contracts.AuthChallenge")]
public record AuthChallenge(string UrlOrForm, bool IsForm = false, string? State = null);
[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Contracts.AuthResult")]
public record AuthResult(bool Success, string? Error = null, string? Details = null);
[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Contracts.ConnectionHealth")]
public record ConnectionHealth(bool Healthy, string? Detail = null, DateTimeOffset Checked = default);
[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Contracts.OAuthCallback")]
public record OAuthCallback(string Code, string State, string? Error = null, string? ErrorDescription = null, string? FallbackRedirectUri = null);
