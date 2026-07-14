using System.Text.Json;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Contracts.Runtime;

public sealed record StoredActionBinding(
    string BindingId,
    string ActionType,
    string InputSchemaRef,
    string RequiredGrant,
    int MaxUses,
    DateTimeOffset ExpiresAt,
    int ActionSchemaVersion = UiProtocol.ActionSchemaVersion);

public sealed record StoredSurfaceRecord(
    long Sequence,
    BrainOwnerId OwnerId,
    ActorId ActorId,
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
    int ActionSchemaVersion = UiProtocol.ActionSchemaVersion);
