using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Compute;
using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Capabilities.Connections.Integrations;
using DigitalBrain.Capabilities.Secrets;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Serialization;

/// <summary>Orleans wire form of <see cref="ConnectionView"/>.</summary>
[GenerateSerializer, Alias("connection.view")]
internal struct ConnectionViewSurrogate
{
    [Id(0)] public string ConnectorId;
    [Id(1)] public string ConnectionId;
    [Id(2)] public string AuthMethodId;
    [Id(3)] public ConnectionStatus Status;
    [Id(4)] public string? Account;
    [Id(5)] public DateTimeOffset? ExpiresAt;
    [Id(6)] public DateTimeOffset? LastProbedAt;
    [Id(7)] public string? Detail;
}

[RegisterConverter]
internal sealed class ConnectionViewSurrogateConverter : IConverter<ConnectionView, ConnectionViewSurrogate>
{
    public ConnectionView ConvertFromSurrogate(in ConnectionViewSurrogate surrogate)
        => new(
            surrogate.ConnectorId,
            surrogate.ConnectionId,
            surrogate.AuthMethodId,
            surrogate.Status,
            surrogate.Account,
            surrogate.ExpiresAt,
            surrogate.LastProbedAt,
            surrogate.Detail);

    public ConnectionViewSurrogate ConvertToSurrogate(in ConnectionView value) => new()
    {
        ConnectorId = value.ConnectorId,
        ConnectionId = value.ConnectionId,
        AuthMethodId = value.AuthMethodId,
        Status = value.Status,
        Account = value.Account,
        ExpiresAt = value.ExpiresAt,
        LastProbedAt = value.LastProbedAt,
        Detail = value.Detail,
    };
}

/// <summary>Orleans wire form of <see cref="OAuthGrant"/>.</summary>
[GenerateSerializer, Alias("connection.oauth-grant")]
internal struct OAuthGrantSurrogate
{
    [Id(0)] public string AuthMethodId;
    [Id(1)] public string AccessToken;
    [Id(2)] public string? RefreshToken;
    [Id(3)] public DateTimeOffset? ExpiresAt;
    [Id(4)] public string Scopes;
    [Id(5)] public string? Target;
    [Id(6)] public string Response;
}

[RegisterConverter]
internal sealed class OAuthGrantSurrogateConverter : IConverter<OAuthGrant, OAuthGrantSurrogate>
{
    public OAuthGrant ConvertFromSurrogate(in OAuthGrantSurrogate surrogate)
        => new(
            surrogate.AuthMethodId,
            surrogate.AccessToken,
            surrogate.RefreshToken,
            surrogate.ExpiresAt,
            surrogate.Scopes,
            surrogate.Target,
            surrogate.Response);

    public OAuthGrantSurrogate ConvertToSurrogate(in OAuthGrant value) => new()
    {
        AuthMethodId = value.AuthMethodId,
        AccessToken = value.AccessToken,
        RefreshToken = value.RefreshToken,
        ExpiresAt = value.ExpiresAt,
        Scopes = value.Scopes,
        Target = value.Target,
        Response = value.Response,
    };
}

/// <summary>Orleans wire form of <see cref="ConnectionProbeResult"/>.</summary>
[GenerateSerializer, Alias("connection.probe-result")]
internal struct ConnectionProbeResultSurrogate
{
    [Id(0)] public ConnectionProbeOutcome Outcome;
    [Id(1)] public string Detail;
}

[RegisterConverter]
internal sealed class ConnectionProbeResultSurrogateConverter : IConverter<ConnectionProbeResult, ConnectionProbeResultSurrogate>
{
    public ConnectionProbeResult ConvertFromSurrogate(in ConnectionProbeResultSurrogate surrogate)
        => new(surrogate.Outcome, surrogate.Detail);

    public ConnectionProbeResultSurrogate ConvertToSurrogate(in ConnectionProbeResult value) => new()
    {
        Outcome = value.Outcome,
        Detail = value.Detail,
    };
}

/// <summary>Orleans wire form of <see cref="IntegrationSnapshot"/>.</summary>
[GenerateSerializer, Alias("integration.registration.snapshot")]
internal struct IntegrationSnapshotSurrogate
{
    [Id(0)] public string IntegrationId;
    [Id(1)] public IntegrationStatus Status;
    [Id(2)] public string[] MissingFields;
    [Id(3)] public Dictionary<string, string> Settings;
    [Id(4)] public long Revision;
}

[RegisterConverter]
internal sealed class IntegrationSnapshotSurrogateConverter : IConverter<IntegrationSnapshot, IntegrationSnapshotSurrogate>
{
    public IntegrationSnapshot ConvertFromSurrogate(in IntegrationSnapshotSurrogate surrogate) => new()
    {
        IntegrationId = surrogate.IntegrationId ?? string.Empty,
        Status = surrogate.Status,
        MissingFields = surrogate.MissingFields ?? [],
        Settings = surrogate.Settings ?? [],
        Revision = surrogate.Revision,
    };

    public IntegrationSnapshotSurrogate ConvertToSurrogate(in IntegrationSnapshot value) => new()
    {
        IntegrationId = value.IntegrationId,
        Status = value.Status,
        MissingFields = value.MissingFields,
        Settings = value.Settings,
        Revision = value.Revision,
    };
}

/// <summary>Orleans wire form of <see cref="ConfigureIntegration"/>.</summary>
[GenerateSerializer, Alias("integration.registration.configure")]
internal struct ConfigureIntegrationSurrogate
{
    [Id(0)] public Dictionary<string, string> Values;
}

[RegisterConverter]
internal sealed class ConfigureIntegrationSurrogateConverter : IConverter<ConfigureIntegration, ConfigureIntegrationSurrogate>
{
    public ConfigureIntegration ConvertFromSurrogate(in ConfigureIntegrationSurrogate surrogate) => new()
    {
        Values = surrogate.Values ?? [],
    };

    public ConfigureIntegrationSurrogate ConvertToSurrogate(in ConfigureIntegration value) => new()
    {
        Values = value.Values,
    };
}

/// <summary>Orleans wire form of <see cref="ReleasedIntegration"/>.</summary>
[GenerateSerializer, Alias("integration.registration.released")]
internal struct ReleasedIntegrationSurrogate
{
    [Id(0)] public Dictionary<string, string> Values;
    [Id(1)] public IntegrationSnapshot Snapshot;
}

[RegisterConverter]
internal sealed class ReleasedIntegrationSurrogateConverter : IConverter<ReleasedIntegration, ReleasedIntegrationSurrogate>
{
    public ReleasedIntegration ConvertFromSurrogate(in ReleasedIntegrationSurrogate surrogate) => new()
    {
        Values = surrogate.Values ?? [],
        Snapshot = surrogate.Snapshot ?? new(),
    };

    public ReleasedIntegrationSurrogate ConvertToSurrogate(in ReleasedIntegration value) => new()
    {
        Values = value.Values,
        Snapshot = value.Snapshot,
    };
}

/// <summary>Orleans wire form of <see cref="Trace"/>.</summary>
[GenerateSerializer, Alias("brain.trace")]
internal struct TraceSurrogate
{
    [Id(0)] public string TraceId;
    [Id(1)] public string PayerAccountId;
    [Id(2)] public string BrainId;
    [Id(3)] public NeuronId Origin;
    [Id(4)] public long? BudgetMicros;
}

[RegisterConverter]
internal sealed class TraceSurrogateConverter : IConverter<Trace, TraceSurrogate>
{
    public Trace ConvertFromSurrogate(in TraceSurrogate surrogate) => new()
    {
        TraceId = surrogate.TraceId,
        PayerAccountId = surrogate.PayerAccountId,
        BrainId = surrogate.BrainId,
        Origin = surrogate.Origin,
        BudgetMicros = surrogate.BudgetMicros,
    };

    public TraceSurrogate ConvertToSurrogate(in Trace value) => new()
    {
        TraceId = value.TraceId,
        PayerAccountId = value.PayerAccountId,
        BrainId = value.BrainId,
        Origin = value.Origin,
        BudgetMicros = value.BudgetMicros,
    };
}

/// <summary>Orleans wire form of <see cref="Owner"/>.</summary>
[GenerateSerializer, Alias("brain.owner")]
internal struct OwnerSurrogate
{
    [Id(0)] public string AccountId;
}

[RegisterConverter]
internal sealed class OwnerSurrogateConverter : IConverter<Owner, OwnerSurrogate>
{
    public Owner ConvertFromSurrogate(in OwnerSurrogate surrogate) => new(surrogate.AccountId);

    public OwnerSurrogate ConvertToSurrogate(in Owner value) => new() { AccountId = value.AccountId };
}

/// <summary>Orleans wire form of <see cref="BrainState"/>.</summary>
[GenerateSerializer, Alias("brain.state")]
internal struct BrainStateSurrogate
{
    [Id(0)] public Owner Owner;
    [Id(1)] public DateTimeOffset CreatedAt;
}

[RegisterConverter]
internal sealed class BrainStateSurrogateConverter : IConverter<BrainState, BrainStateSurrogate>
{
    public BrainState ConvertFromSurrogate(in BrainStateSurrogate surrogate) => new(surrogate.Owner, surrogate.CreatedAt);

    public BrainStateSurrogate ConvertToSurrogate(in BrainState value) => new()
    {
        Owner = value.Owner,
        CreatedAt = value.CreatedAt,
    };
}

/// <summary>Orleans wire form of <see cref="SealedSecret"/>.</summary>
[GenerateSerializer, Alias("sealed-secret")]
internal struct SealedSecretSurrogate
{
    [Id(0)] public string Cipher;
    [Id(1)] public string KeyId;
}

[RegisterConverter]
internal sealed class SealedSecretSurrogateConverter : IConverter<SealedSecret, SealedSecretSurrogate>
{
    public SealedSecret ConvertFromSurrogate(in SealedSecretSurrogate surrogate) => new()
    {
        Cipher = surrogate.Cipher,
        KeyId = surrogate.KeyId,
    };

    public SealedSecretSurrogate ConvertToSurrogate(in SealedSecret value) => new()
    {
        Cipher = value.Cipher,
        KeyId = value.KeyId,
    };
}
