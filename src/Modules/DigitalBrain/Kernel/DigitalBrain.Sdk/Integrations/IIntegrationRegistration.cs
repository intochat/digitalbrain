using System.Text;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using Orleans.Concurrency;

// SDK contracts retain their original namespace and wire identities for compatibility.
namespace DigitalBrain.Platform.Integrations;

public enum RegistrationStatus
{
    Unconfigured = 0,
    Partial = 1,
    Ready = 2,
}

// Key: "integration/{id}". One deployment-level provider registration (a Google OAuth app, an AI key);
// every value lives in the vault and never appears in state, snapshots or signals.
[Alias("integration.registration")]
[PlatformOnly]
[Orleans.Metadata.DefaultGrainType("integration.registration")]
public interface IIntegrationRegistration : INeuron
{
    [ReadOnly]
    Task<RegistrationSnapshot> Read();

    Task<RegistrationSnapshot> Configure(ConfigureRegistration request);

    Task<RegistrationSnapshot> Clear(string field);

    // Checks and writes atomically inside the grain: applies only while the registration is Unconfigured.
    Task<RegistrationSnapshot> SeedIfUnconfigured(ConfigureRegistration request);

    Task<ReleasedRegistration> Release(CallerContext caller);
}

[GenerateSerializer, Alias("integration.registration.snapshot")]
public sealed record RegistrationSnapshot
{
    [Id(0)] public string IntegrationId { get; init; } = "";
    [Id(1)] public RegistrationStatus Status { get; init; }
    [Id(2)] public string[] MissingFields { get; init; } = [];
    [Id(3)] public Dictionary<string, string> Settings { get; init; } = [];
    [Id(4)] public long Revision { get; init; }
}

// Field name to value. The values go to the vault immediately, and the record never prints them.
[GenerateSerializer, Alias("integration.registration.configure")]
public sealed record ConfigureRegistration
{
    [Id(0)] public Dictionary<string, string> Values { get; init; } = [];

    private bool PrintMembers(StringBuilder builder) => builder.Append("Values = <redacted>") is not null;
}

// Handed only to trusted platform code; never persisted.
[GenerateSerializer, Alias("integration.registration.released")]
public sealed record ReleasedRegistration
{
    [Id(0)] public Dictionary<string, string> Values { get; init; } = [];

    private bool PrintMembers(StringBuilder builder) => builder.Append("Values = <redacted>") is not null;
}

[GenerateSerializer, Alias("integration.registered")]
public sealed record RegistrationChanged(string IntegrationId, RegistrationStatus Status) : Signal;
