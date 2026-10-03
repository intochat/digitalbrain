using DigitalBrain.Contracts.Enforcement;

namespace DigitalBrain.Platform.Contracts.Integrations;

public sealed record Capability(string IntegrationId, string Kind, string Id, string Display);

// Capabilities are derived on every call from live registrations and accounts, never stored.
public interface ICapabilities
{
    Task<Capability[]> List(CallerContext caller, string? kind = null, CancellationToken ct = default);
}

// A module contributes the kinds of capability its integrations provide.
public interface ICapabilitySource
{
    Task<Capability[]> Derive(CallerContext caller, CancellationToken ct);
}
