using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

public sealed record SurfaceActionToken(string Token, DateTimeOffset ExpiresAt);

public sealed class SurfaceEnvelopeWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Write(
        RequestContext recipient,
        StoredSurfaceRecord record,
        IReadOnlySet<string> clientCapabilities,
        IReadOnlyDictionary<string, SurfaceActionToken> actionTokens)
    {
        DemandVisible(recipient, record);
        if (record.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("An expired surface cannot be materialized.");
        if (record.ProtocolVersion != UiProtocol.ProtocolVersion ||
            record.SurfaceSchemaVersion != UiProtocol.SurfaceSchemaVersion ||
            record.ActionSchemaVersion != UiProtocol.ActionSchemaVersion ||
            !string.Equals(record.SurfaceSchema, UiProtocol.SurfaceSchema, StringComparison.Ordinal))
            throw new InvalidOperationException("The stored surface protocol metadata is unsupported.");
        try { SurfacePayloadPolicy.DemandSafe(record.Payload); }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The stored surface payload violates presentation policy.", exception);
        }
        var missing = record.RequiredClientCapabilities.Where(required => !clientCapabilities.Contains(required)).ToArray();
        if (missing.Length > 0) throw new SurfaceCapabilityException(missing);

        var wireActions = record.Audience.Kind == SurfaceAudienceKind.Principal
            ? record.Actions
                .Where(binding => actionTokens.ContainsKey(binding.BindingId))
                .Select(binding =>
                {
                    var issued = actionTokens[binding.BindingId];
                    return (object)new
                    {
                        actionSchemaVersion = binding.ActionSchemaVersion,
                        bindingId = binding.BindingId,
                        actionType = binding.ActionType,
                        actionToken = issued.Token,
                        surfaceId = record.SurfaceId,
                        surfaceRevision = record.Revision,
                        expiresAt = issued.ExpiresAt.ToUniversalTime().ToString("O")
                    };
                }).ToArray()
            : [];

        return JsonSerializer.Serialize(new
        {
            protocolVersion = record.ProtocolVersion,
            surfaceSchema = record.SurfaceSchema,
            surfaceSchemaVersion = record.SurfaceSchemaVersion,
            surfaceId = record.SurfaceId,
            revision = record.Revision,
            tenantId = record.TenantId.Value,
            workspaceId = record.WorkspaceId.Value,
            audience = new { kind = record.Audience.Kind.ToString().ToLowerInvariant(), id = record.Audience.Id },
            feedSequence = record.Sequence,
            createdAt = record.CreatedAt.ToUniversalTime().ToString("O"),
            expiresAt = record.ExpiresAt?.ToUniversalTime().ToString("O"),
            correlationId = record.CorrelationId,
            cause = new { kind = record.CauseKind, id = record.CauseId },
            requiredClientCapabilities = record.RequiredClientCapabilities,
            contentHash = record.ContentHash,
            payload = record.Payload,
            actions = wireActions
        }, JsonOptions);
    }

    private static void DemandVisible(RequestContext recipient, StoredSurfaceRecord record)
    {
        if (record.TenantId != recipient.TenantId || record.WorkspaceId != recipient.WorkspaceId)
            throw new UnauthorizedAccessException("Surface scope denied.");
        var visible = record.Audience.Kind switch
        {
            SurfaceAudienceKind.Principal =>
                record.AudiencePrincipalKind == recipient.Principal.Kind &&
                string.Equals(record.Audience.Id, PrincipalScope.Id(recipient.Principal), StringComparison.Ordinal),
            SurfaceAudienceKind.Workspace => string.Equals(record.Audience.Id, recipient.WorkspaceId.Value, StringComparison.Ordinal),
            SurfaceAudienceKind.Public => string.IsNullOrEmpty(record.Audience.Id),
            _ => false
        };
        if (!visible) throw new UnauthorizedAccessException("Surface audience denied.");
    }
}

public sealed class SurfaceCapabilityException(IReadOnlyList<string> missing)
    : InvalidOperationException("The client does not support required surface capabilities.")
{
    public IReadOnlyList<string> Missing { get; } = missing;
}
