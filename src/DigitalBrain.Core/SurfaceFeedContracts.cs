using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

/// <summary>A token-free action description safe to persist with a surface record.</summary>
public sealed record StoredActionBinding(
    string BindingId,
    string ActionType,
    string InputSchemaRef,
    string RequiredGrant,
    int MaxUses,
    DateTimeOffset ExpiresAt,
    int ActionSchemaVersion = UiProtocol.ActionSchemaVersion);

/// <summary>
/// Durable UI record. Wire action tokens are deliberately absent and are minted for the authenticated
/// recipient each time this record is delivered.
/// </summary>
public sealed record StoredSurfaceRecord(
    long Sequence,
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    SurfaceAudience Audience,
    string SurfaceId,
    int Revision,
    string ContentHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string CorrelationId,
    string CauseKind,
    string CauseId,
    IReadOnlyList<string> RequiredClientCapabilities,
    JsonElement Payload,
    IReadOnlyList<StoredActionBinding> Actions,
    int ProtocolVersion = UiProtocol.ProtocolVersion,
    string SurfaceSchema = UiProtocol.SurfaceSchema,
    int SurfaceSchemaVersion = UiProtocol.SurfaceSchemaVersion,
    int ActionSchemaVersion = UiProtocol.ActionSchemaVersion,
    PrincipalKind? AudiencePrincipalKind = null);
