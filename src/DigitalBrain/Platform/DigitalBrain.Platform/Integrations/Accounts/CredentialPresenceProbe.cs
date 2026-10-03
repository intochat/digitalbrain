using DigitalBrain.Platform.Contracts.Integrations.Accounts;
namespace DigitalBrain.Platform.Integrations.Accounts;

internal sealed class CredentialPresenceProbe : IAccountProbe
{
    public Task<AccountProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default)
        => Task.FromResult(string.IsNullOrWhiteSpace(credential)
            ? AccountProbeResult.Failing("No credential is stored for this connector.")
            : AccountProbeResult.Connected("A credential is stored; the source has not been verified."));
}
