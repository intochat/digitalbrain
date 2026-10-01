// SDK contracts retain their original namespace and wire identities for compatibility.
namespace DigitalBrain.Platform.Integrations.Accounts;

public enum AccountProbeOutcome
{
    Connected = 0,
    Expired = 1,
    Failing = 2,
}

[GenerateSerializer, Alias("connections.probe-result")] // alias predates the integrations rename; persisted, do not touch
public sealed record AccountProbeResult(AccountProbeOutcome Outcome, string Detail)
{
    public static AccountProbeResult Connected(string detail = "The connector probe succeeded.") => new(AccountProbeOutcome.Connected, detail);

    public static AccountProbeResult Expired(string detail) => new(AccountProbeOutcome.Expired, detail);

    public static AccountProbeResult Failing(string detail) => new(AccountProbeOutcome.Failing, detail);
}

// Integrations can replace the default presence check with a source-specific probe.
public interface IAccountProbe
{
    Task<AccountProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("connections.not-configured")] // alias predates the integrations rename; persisted, do not touch
public sealed class AccountNotConfiguredException(string message) : InvalidOperationException(message);
