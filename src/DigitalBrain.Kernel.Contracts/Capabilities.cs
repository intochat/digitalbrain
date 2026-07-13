using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans;

namespace DigitalBrain.Kernel.Contracts;

[GenerateSerializer, Alias("digitalbrain.v3.capability-request")]
public sealed record CapabilityRequest
{
    public const int MaximumPayloadBytes = 64 * 1024;

    [JsonConstructor]
    public CapabilityRequest(
        BrainOwnerId ownerId,
        ActorId actorId,
        FeatureInstallationId installationId,
        ReleaseDigest releaseDigest,
        string inputId,
        string logicalOperationKey,
        string capabilityId,
        int capabilityVersion,
        ProviderConnectionId? providerConnectionId,
        GrantRevision grantRevision,
        JsonElement payload,
        DateTimeOffset deadline,
        string correlationId,
        string? causationId)
    {
        if (string.IsNullOrEmpty(ownerId.Value)) throw new ArgumentException("An owner is required.", nameof(ownerId));
        if (string.IsNullOrEmpty(actorId.Value)) throw new ArgumentException("An actor is required.", nameof(actorId));
        if (string.IsNullOrEmpty(installationId.Value)) throw new ArgumentException("An installation is required.", nameof(installationId));
        if (string.IsNullOrEmpty(releaseDigest.Value)) throw new ArgumentException("A release digest is required.", nameof(releaseDigest));
        OwnerId = ownerId;
        ActorId = actorId;
        InstallationId = installationId;
        ReleaseDigest = releaseDigest;
        InputId = ContractValue.Identifier(inputId, nameof(inputId));
        LogicalOperationKey = ContractValue.Identifier(logicalOperationKey, nameof(logicalOperationKey));
        CapabilityId = ContractValue.Identifier(capabilityId, nameof(capabilityId));
        ArgumentOutOfRangeException.ThrowIfLessThan(capabilityVersion, 1);
        CapabilityVersion = capabilityVersion;
        if (providerConnectionId is { } connection && string.IsNullOrEmpty(connection.Value))
            throw new ArgumentException("A non-default provider connection is required.", nameof(providerConnectionId));
        ProviderConnectionId = providerConnectionId;
        if (grantRevision.Value < 1)
            throw new ArgumentException("A grant revision is required.", nameof(grantRevision));
        GrantRevision = grantRevision;
        Payload = BoundedPayload(payload);
        if (deadline == default) throw new ArgumentException("A capability deadline is required.", nameof(deadline));
        Deadline = deadline;
        CorrelationId = ContractValue.Identifier(correlationId, nameof(correlationId));
        CausationId = causationId is null ? null : ContractValue.Identifier(causationId, nameof(causationId));
    }

    [Id(0)] public BrainOwnerId OwnerId { get; }
    [Id(1)] public ActorId ActorId { get; }
    [Id(2)] public FeatureInstallationId InstallationId { get; }
    [Id(3)] public ReleaseDigest ReleaseDigest { get; }
    [Id(4)] public string InputId { get; }
    [Id(5)] public string LogicalOperationKey { get; }
    [Id(6)] public string CapabilityId { get; }
    [Id(7)] public int CapabilityVersion { get; }
    [Id(8)] public ProviderConnectionId? ProviderConnectionId { get; }
    [Id(9)] public GrantRevision GrantRevision { get; }
    [Id(10)] public JsonElement Payload { get; }
    [Id(11)] public DateTimeOffset Deadline { get; }
    [Id(12)] public string CorrelationId { get; }
    [Id(13)] public string? CausationId { get; }

    private static JsonElement BoundedPayload(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A JSON payload is required.", nameof(payload));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (bytes.Length > MaximumPayloadBytes)
            throw new ArgumentException("The capability payload exceeds 64 KiB.", nameof(payload));
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }
}
