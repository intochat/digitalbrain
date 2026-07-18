using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Orleans;
namespace DigitalBrain.Kernel.Runtime;

[GenerateSerializer, Alias("digitalbrain.runtime.surface-feed-identity")]
public sealed record SurfaceFeedIdentity([property: Id(0)] BrainOwnerId OwnerId, [property: Id(1)] ActorId ActorId);
[GenerateSerializer, Alias("digitalbrain.runtime.surface-action-binding")]
public sealed record SurfaceActionBinding(
    [property: Id(0)] string BindingId,
    [property: Id(1)] string SurfaceId,
    [property: Id(2)] int SurfaceRevision,
    [property: Id(3)] string ActionType,
    [property: Id(4)] string InputSchemaRef,
    [property: Id(5)] string RequiredGrant,
    [property: Id(6)] int ActionSchemaVersion,
    [property: Id(7)] string TokenHash,
    [property: Id(8)] int MaxUses,
    [property: Id(9)] int Uses,
    [property: Id(10)] DateTimeOffset ExpiresAt,
    [property: Id(11)] string? LastIdempotencyKey,
    [property: Id(12)] string? LastOperationId);
[GenerateSerializer, Alias("digitalbrain.runtime.surface-feed-projection")]
public sealed record SurfaceFeedProjection(
    [property: Id(0)] string ProjectionId,
    [property: Id(1)] string SurfaceId,
    [property: Id(2)] int SurfaceRevision,
    [property: Id(3)] string ContentHash,
    [property: Id(4)] byte[] PayloadUtf8,
    [property: Id(5)] DateTimeOffset CreatedAt,
    [property: Id(6)] DateTimeOffset? ExpiresAt,
    [property: Id(7)] SurfaceActionBinding[] ActionBindings);
[GenerateSerializer, Alias("digitalbrain.runtime.home-surface-bootstrap")]
public sealed record HomeSurfaceBootstrap(
    [property: Id(0)] string ProjectionId,
    [property: Id(1)] string ConversationId,
    [property: Id(2)] string CorrelationId,
    [property: Id(3)] DateTimeOffset CreatedAt);
public sealed record SurfaceFeedPresentation(
    string CorrelationId,
    string CauseKind,
    string CauseId,
    string[] RequiredClientCapabilities,
    JsonElement Payload,
    int ConversationRevision = 0,
    int PresentationVersion = 0)
{
    public const int CurrentVersion = 2;
}
public static class SurfaceFeedPresentationCompatibility
{
    private const int VersionOne = 1;
    private static readonly string[] LegacyProperties =
    [
        nameof(SurfaceFeedPresentation.CorrelationId),
        nameof(SurfaceFeedPresentation.CauseKind),
        nameof(SurfaceFeedPresentation.CauseId),
        nameof(SurfaceFeedPresentation.RequiredClientCapabilities),
        nameof(SurfaceFeedPresentation.Payload)
    ];
    private static readonly string[] VersionedProperties =
    [.. LegacyProperties, nameof(SurfaceFeedPresentation.ConversationRevision), nameof(SurfaceFeedPresentation.PresentationVersion)];
    public static bool HasSupportedShape(JsonElement root, SurfaceFeedPresentation presentation)
    {
        var hasRevision = root.TryGetProperty(nameof(SurfaceFeedPresentation.ConversationRevision), out var revision);
        var hasVersion = root.TryGetProperty(nameof(SurfaceFeedPresentation.PresentationVersion), out var version);
        if (hasRevision && hasVersion)
        {
            if (revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt32(out var storedRevision) || storedRevision < 0 ||
                storedRevision != presentation.ConversationRevision ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var storedVersion) ||
                storedVersion != presentation.PresentationVersion)
                return false;
            return storedVersion == SurfaceFeedPresentation.CurrentVersion ||
                   storedVersion == VersionOne && HasExactProperties(root, VersionedProperties);
        }
        if (hasRevision || hasVersion || presentation.ConversationRevision != 0 || presentation.PresentationVersion != 0)
            return false;
        return HasExactProperties(root, LegacyProperties);
    }
    private static bool HasExactProperties(JsonElement root, IReadOnlyCollection<string> expected)
    {
        var properties = root.EnumerateObject().Select(property => property.Name).ToArray();
        return properties.Length == expected.Count && properties.Distinct(StringComparer.Ordinal).Count() == expected.Count &&
               properties.ToHashSet(StringComparer.Ordinal).SetEquals(expected);
    }
}
[GenerateSerializer, Alias("digitalbrain.runtime.surface-feed-record")]
public sealed record SurfaceFeedRecord(
    [property: Id(0)] long Sequence,
    [property: Id(1)] string ProjectionId,
    [property: Id(2)] string SurfaceId,
    [property: Id(3)] int SurfaceRevision,
    [property: Id(4)] string ContentHash,
    [property: Id(5)] byte[] PayloadUtf8,
    [property: Id(6)] DateTimeOffset CreatedAt,
    [property: Id(7)] DateTimeOffset? ExpiresAt);
[GenerateSerializer, Alias("digitalbrain.runtime.surface-feed-ack")]
public sealed record SurfaceFeedAckCursor(
    [property: Id(0)] string SessionScopeHash,
    [property: Id(1)] long Sequence,
    [property: Id(2)] DateTimeOffset ExpiresAt,
    [property: Id(3)] DateTimeOffset UpdatedAt);
[GenerateSerializer, Alias("digitalbrain.runtime.surface-delivery-record")]
public sealed record SurfaceDeliveryRecord([property: Id(0)] string DeliveryId, [property: Id(1)] long Sequence, [property: Id(2)] DateTimeOffset DeliveredAt);
[GenerateSerializer, Alias("digitalbrain.runtime.surface-feed-state")]
public sealed record SurfaceFeedState(
    [property: Id(0)] int SchemaVersion,
    [property: Id(1)] long Revision,
    [property: Id(2)] SurfaceFeedIdentity? Identity,
    [property: Id(3)] long LastSequence,
    [property: Id(4)] long RebuildEpoch,
    [property: Id(5)] SurfaceFeedRecord[] CurrentSurfaces,
    [property: Id(6)] SurfaceActionBinding[] ActionBindings,
    [property: Id(7)] string[] AppliedProjectionIds,
    [property: Id(8)] SurfaceDeliveryRecord[] DeliveryDedupe,
    [property: Id(9)] SurfaceFeedAckCursor[] Acknowledgements)
{
    [Id(10)] public SurfaceFeedRecord[] EventHistory { get; init; } = [];
    public static SurfaceFeedState Empty() => new(RuntimeStateSchemas.SurfaceFeed, 0, null, 0, 0, [], [], [], [], []);
}
[GenerateSerializer, Alias("digitalbrain.runtime.surface-action-consumption")]
public sealed record SurfaceActionConsumption(
    [property: Id(0)] SurfaceFeedState State,
    [property: Id(1)] string OperationId,
    [property: Id(2)] bool Consumed,
    [property: Id(3)] SurfaceActionBinding AuthorizedBinding);
[Alias("digitalbrain.runtime.i-surface-feed-neuron")]
public interface ISurfaceFeedNeuron : IGrainWithStringKey
{
    [Alias("digitalbrain.runtime.surface-feed.read")]
    Task<SurfaceFeedState> ReadAsync();
    [Alias("digitalbrain.runtime.surface-feed.initialize")]
    Task<SurfaceFeedState> InitializeAsync(long expectedRevision, SurfaceFeedIdentity identity);
    [Alias("digitalbrain.runtime.surface-feed.ensure-home-surface")]
    Task<SurfaceFeedState> EnsureHomeSurfaceAsync(long expectedRevision, HomeSurfaceBootstrap bootstrap);
    [Alias("digitalbrain.runtime.surface-feed.apply-projection")]
    Task<SurfaceFeedState> ApplyProjectionAsync(long expectedRevision, SurfaceFeedProjection projection, DateTimeOffset now);
    [Alias("digitalbrain.runtime.surface-feed.record-delivery")]
    Task<SurfaceFeedState> RecordDeliveryAsync(long expectedRevision, string deliveryId, long sequence, DateTimeOffset deliveredAt);
    [Alias("digitalbrain.runtime.surface-feed.acknowledge")]
    Task<SurfaceFeedState> AcknowledgeAsync(long expectedRevision, string sessionScopeHash, long sequence, DateTimeOffset cursorExpiresAt, DateTimeOffset now);
    [Alias("digitalbrain.runtime.surface-feed.revoke-session")]
    Task<SurfaceFeedState> RevokeSessionAsync(long expectedRevision, string sessionScopeHash, DateTimeOffset now);
    [Alias("digitalbrain.runtime.surface-feed.consume-action")]
    Task<SurfaceActionConsumption> ConsumeActionAsync(long expectedRevision, string bindingId, string tokenHash, string idempotencyKey, string operationId, DateTimeOffset now);
    [Alias("digitalbrain.runtime.surface-feed.renew-action-bindings")]
    Task<SurfaceFeedState> RenewActionBindingsAsync(long expectedRevision, DateTimeOffset now);
    [Alias("digitalbrain.runtime.surface-feed.rebuild")]
    Task<SurfaceFeedState> RebuildAsync(long expectedRevision, string projectionId, DateTimeOffset now);
}
public static class SurfaceFeedTransitions
{
    private const string LegacyNewConversationBindingId = "ino.new";
    private const string LegacyDeleteConversationBindingId = "ino.delete";
    public const int MaximumCurrentSurfaces = 64;
    public const int MaximumActionBindings = 256;
    public const int MaximumProjectionDedupe = 512;
    public const int MaximumDeliveryDedupe = 512;
    public const int MaximumAcknowledgements = 256;
    public const int MaximumEventHistory = 512;
    public const int MaximumEventHistoryPayloadBytes = 1 * 1024 * 1024;
    public static SurfaceFeedState Initialize(SurfaceFeedState state, long expectedRevision, SurfaceFeedIdentity identity)
    {
        DemandRevision(state, expectedRevision);
        ValidateIdentity(identity);
        if (state.Identity is not null)
        {
            if (state.Identity == identity) return state;
            throw new InvalidOperationException("A surface-feed grain cannot be rebound to another identity.");
        }
        return ValidateAndCompact(state with { Revision = checked(state.Revision + 1), Identity = identity }, DateTimeOffset.MinValue);
    }
    public static SurfaceFeedState EnsureHomeSurface(SurfaceFeedState state, long expectedRevision, HomeSurfaceBootstrap bootstrap)
    {
        DemandMutable(state, expectedRevision);
        ValidateHomeSurfaceBootstrap(state, bootstrap);
        if (state.CurrentSurfaces.Length > 0) return state;
        var conversation = new InoConversationSnapshot(bootstrap.ConversationId, 0, [], []);
        var descriptors = ConversationSurfacePayload.Actions(conversation, bootstrap.CreatedAt);
        var payload = ConversationSurfacePayload.Build(conversation);
        var presentation = new SurfaceFeedPresentation(
            bootstrap.CorrelationId,
            "conversation",
            bootstrap.ConversationId,
            ConversationSurfacePayload.RequiredCapabilities,
            payload,
            conversation.Revision,
            SurfaceFeedPresentation.CurrentVersion);
        var projection = new SurfaceFeedProjection(
            bootstrap.ProjectionId,
            ConversationSurfacePayload.HomeSurfaceId,
            1,
            SurfaceContentHash.Compute(payload, descriptors),
            JsonSerializer.SerializeToUtf8Bytes(presentation),
            bootstrap.CreatedAt,
            null,
            CreateHomeBindings(descriptors, 1));
        return ApplyProjection(state, expectedRevision, projection, bootstrap.CreatedAt);
    }
    public static SurfaceFeedState ApplyProjection(SurfaceFeedState state, long expectedRevision, SurfaceFeedProjection projection, DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        ValidateProjection(projection);
        if (state.AppliedProjectionIds.Contains(projection.ProjectionId, StringComparer.Ordinal)) return state;
        var current = state.CurrentSurfaces.FirstOrDefault(surface =>
            string.Equals(surface.SurfaceId, projection.SurfaceId, StringComparison.Ordinal));
        if (current is not null && projection.SurfaceRevision < current.SurfaceRevision)
            throw new RuntimeStateConflictException(projection.SurfaceRevision, current.SurfaceRevision);
        if (current is not null && projection.SurfaceRevision == current.SurfaceRevision &&
            !string.Equals(projection.ContentHash, current.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A surface revision cannot change content.");
        var surfaces = state.CurrentSurfaces;
        var bindings = state.ActionBindings;
        var history = state.EventHistory ?? [];
        var lastSequence = state.LastSequence;
        if (current is null || projection.SurfaceRevision > current.SurfaceRevision)
        {
            lastSequence = checked(lastSequence + 1);
            var record = new SurfaceFeedRecord(
                lastSequence,
                projection.ProjectionId,
                projection.SurfaceId,
                projection.SurfaceRevision,
                projection.ContentHash.ToLowerInvariant(),
                projection.PayloadUtf8.ToArray(),
                projection.CreatedAt,
                projection.ExpiresAt);
            surfaces = surfaces.Where(surface => !string.Equals(surface.SurfaceId, projection.SurfaceId, StringComparison.Ordinal))
                .Append(record).ToArray();
            history = history.Append(record).ToArray();
            bindings = bindings.Where(binding => !string.Equals(binding.SurfaceId, projection.SurfaceId, StringComparison.Ordinal))
                .Concat(projection.ActionBindings.Select(binding => binding with { TokenHash = binding.TokenHash.ToLowerInvariant() }))
                .ToArray();
        }
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            LastSequence = lastSequence,
            CurrentSurfaces = surfaces,
            EventHistory = history,
            ActionBindings = bindings,
            AppliedProjectionIds = state.AppliedProjectionIds.Append(projection.ProjectionId).ToArray()
        }, now);
    }
    public static SurfaceFeedState RecordDelivery(SurfaceFeedState state, long expectedRevision, string deliveryId, long sequence, DateTimeOffset deliveredAt)
    {
        DemandMutable(state, expectedRevision);
        DemandId(deliveryId, nameof(deliveryId));
        if (sequence < 1 || sequence > state.LastSequence) throw new ArgumentOutOfRangeException(nameof(sequence));
        var existing = state.DeliveryDedupe.FirstOrDefault(delivery => string.Equals(delivery.DeliveryId, deliveryId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (existing.Sequence == sequence) return state;
            throw new InvalidOperationException("A delivery id cannot move to another sequence.");
        }
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            DeliveryDedupe = state.DeliveryDedupe.Append(new(deliveryId, sequence, deliveredAt)).ToArray()
        }, deliveredAt);
    }
    public static SurfaceFeedState Acknowledge(SurfaceFeedState state, long expectedRevision, string sessionScopeHash, long sequence, DateTimeOffset cursorExpiresAt, DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        RuntimeStateKeys.DemandScopeHash(sessionScopeHash);
        if (sequence < 1 || sequence > state.LastSequence || cursorExpiresAt <= now)
            throw new ArgumentException("Acknowledgements require a delivered feed sequence and future cursor expiry.");
        var cursors = state.Acknowledgements.Where(cursor => cursor.ExpiresAt > now).ToArray();
        var prior = cursors.FirstOrDefault(cursor => string.Equals(cursor.SessionScopeHash, sessionScopeHash, StringComparison.Ordinal));
        if (prior is not null && sequence <= prior.Sequence && cursorExpiresAt <= prior.ExpiresAt) return state;
        var updated = new SurfaceFeedAckCursor(
            sessionScopeHash,
            Math.Max(sequence, prior?.Sequence ?? 0),
            prior is null || cursorExpiresAt > prior.ExpiresAt ? cursorExpiresAt : prior.ExpiresAt,
            now);
        cursors = cursors.Where(cursor => !string.Equals(cursor.SessionScopeHash, sessionScopeHash, StringComparison.Ordinal))
            .Append(updated).ToArray();
        return ValidateAndCompact(state with { Revision = checked(state.Revision + 1), Acknowledgements = cursors }, now);
    }
    public static SurfaceFeedState RevokeSession(SurfaceFeedState state, long expectedRevision, string sessionScopeHash, DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        RuntimeStateKeys.DemandScopeHash(sessionScopeHash);
        var cursors = state.Acknowledgements.Where(cursor => cursor.ExpiresAt > now && !string.Equals(cursor.SessionScopeHash, sessionScopeHash, StringComparison.Ordinal)).ToArray();
        if (cursors.Length == state.Acknowledgements.Length) return state;
        return ValidateAndCompact(state with { Revision = checked(state.Revision + 1), Acknowledgements = cursors }, now);
    }
    public static SurfaceActionConsumption ConsumeAction(SurfaceFeedState state, long expectedRevision, string bindingId, string tokenHash, string idempotencyKey, string operationId, DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        DemandId(bindingId, nameof(bindingId));
        DemandId(idempotencyKey, nameof(idempotencyKey));
        DemandId(operationId, nameof(operationId));
        DemandHash(tokenHash, nameof(tokenHash));
        var binding = state.ActionBindings.FirstOrDefault(candidate => string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal))
                      ?? throw new KeyNotFoundException("Surface action binding not found.");
        if (binding.ExpiresAt <= now || !FixedTimeHashEquals(binding.TokenHash, tokenHash))
            throw new UnauthorizedAccessException("Surface action authorization failed.");
        var current = state.CurrentSurfaces.FirstOrDefault(surface => string.Equals(surface.SurfaceId, binding.SurfaceId, StringComparison.Ordinal));
        if (current is null || current.SurfaceRevision != binding.SurfaceRevision)
            throw new RuntimeStateConflictException(binding.SurfaceRevision, current?.SurfaceRevision ?? 0);
        if (string.Equals(binding.LastIdempotencyKey, idempotencyKey, StringComparison.Ordinal))
            return new(state, binding.LastOperationId!, false, binding);
        if (binding.Uses >= binding.MaxUses) throw new InvalidOperationException("Surface action usage limit exceeded.");
        var updated = binding with { Uses = checked(binding.Uses + 1), LastIdempotencyKey = idempotencyKey, LastOperationId = operationId };
        var next = ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            ActionBindings = state.ActionBindings.Select(candidate => candidate == binding ? updated : candidate).ToArray()
        }, now);
        return new(next, operationId, true, updated);
    }
    public static SurfaceFeedState RenewActionBindings(SurfaceFeedState state, long expectedRevision, DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        var surface = state.CurrentSurfaces.FirstOrDefault(candidate =>
            string.Equals(candidate.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        if (surface is null)
            return state;
        if (!TryReadConversationActions(surface, now, out var presentation, out var descriptors) &&
            !TryRecoverConversationActions(surface, now, out presentation, out descriptors))
            return state;
        var currentBindings = state.ActionBindings.Where(binding =>
            string.Equals(binding.SurfaceId, surface.SurfaceId, StringComparison.Ordinal) &&
            binding.SurfaceRevision == surface.SurfaceRevision).ToArray();
        var renewalThreshold = now.Add(TimeSpan.FromTicks(UiProtocol.ActionTokenLifetime.Ticks / 2));
        if (!RequiresActionAuthorityEvent(currentBindings, descriptors, renewalThreshold)) return state;
        var surfaceRevision = checked(surface.SurfaceRevision + 1);
        var sequence = checked(state.LastSequence + 1);
        var bindings = CreateHomeBindings(descriptors, surfaceRevision);
        var normalizedPresentation = presentation with { PresentationVersion = SurfaceFeedPresentation.CurrentVersion };
        var record = new SurfaceFeedRecord(
            sequence,
            $"surface-action-authority-{sequence}",
            surface.SurfaceId,
            surfaceRevision,
            SurfaceContentHash.Compute(presentation.Payload, descriptors),
            JsonSerializer.SerializeToUtf8Bytes(normalizedPresentation),
            now,
            surface.ExpiresAt);
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            LastSequence = sequence,
            CurrentSurfaces = state.CurrentSurfaces.Where(candidate => !string.Equals(candidate.SurfaceId, surface.SurfaceId, StringComparison.Ordinal))
                .Append(record).ToArray(),
            EventHistory = (state.EventHistory ?? []).Append(record).ToArray(),
            ActionBindings = state.ActionBindings.Where(binding => !string.Equals(binding.SurfaceId, surface.SurfaceId, StringComparison.Ordinal))
                .Concat(bindings).ToArray()
        }, now);
    }
    private static bool TryReadConversationActions(SurfaceFeedRecord surface, DateTimeOffset now, out SurfaceFeedPresentation presentation, out IReadOnlyList<StoredActionBinding> descriptors)
    {
        presentation = null!;
        descriptors = [];
        try
        {
            using var document = JsonDocument.Parse(surface.PayloadUtf8);
            var parsed = document.RootElement.Deserialize<SurfaceFeedPresentation>();
            if (parsed is null || !SurfaceFeedPresentationCompatibility.HasSupportedShape(document.RootElement, parsed))
                return false;
            presentation = parsed;
        }
        catch (JsonException)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(presentation.CorrelationId) ||
            !string.Equals(presentation.CauseKind, "conversation", StringComparison.Ordinal) ||
            !IsCanonicalConversationId(presentation.CauseId) ||
            presentation.RequiredClientCapabilities is null ||
            !presentation.RequiredClientCapabilities.ToHashSet(StringComparer.Ordinal)
                .SetEquals(ConversationSurfacePayload.RequiredCapabilities) ||
            !ConversationSurfacePayload.TryActions(presentation.Payload, now, out descriptors))
            return false;
        return true;
    }
    private static bool TryRecoverConversationActions(SurfaceFeedRecord surface, DateTimeOffset now, out SurfaceFeedPresentation presentation, out IReadOnlyList<StoredActionBinding> descriptors)
    {
        presentation = null!;
        descriptors = [];
        try
        {
            using var document = JsonDocument.Parse(surface.PayloadUtf8);
            var root = document.RootElement;
            if (!TryReadPresentationProperty(root, "Payload", "payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object ||
                !ConversationSurfacePayload.TryActions(payload, now, out descriptors))
                return false;
            if (descriptors.Count == 0)
                return false;
            TryReadPresentationProperty(root, "CorrelationId", "correlationId", out var correlationProperty);
            TryReadPresentationProperty(root, "CauseKind", "causeKind", out var causeKindProperty);
            TryReadPresentationProperty(root, "CauseId", "causeId", out var causeIdProperty);
            var correlationId = correlationProperty.ValueKind == JsonValueKind.String ? correlationProperty.GetString() : null;
            var causeKind = causeKindProperty.ValueKind == JsonValueKind.String ? causeKindProperty.GetString() : null;
            var causeId = causeIdProperty.ValueKind == JsonValueKind.String ? causeIdProperty.GetString() : null;
            if (string.IsNullOrWhiteSpace(correlationId) ||
                !string.Equals(causeKind, "conversation", StringComparison.Ordinal) ||
                !IsCanonicalConversationId(causeId))
                return false;
            presentation = new SurfaceFeedPresentation(
                correlationId,
                causeKind,
                causeId!,
                ConversationSurfacePayload.RequiredCapabilities,
                payload.Clone(),
                0,
                SurfaceFeedPresentation.CurrentVersion);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
    private static bool TryReadPresentationProperty(JsonElement root, string pascalName, string camelName, out JsonElement value)
    {
        if (root.TryGetProperty(pascalName, out value) || root.TryGetProperty(camelName, out value))
            return true;
        value = default;
        return false;
    }
    private static bool RequiresActionAuthorityEvent(IReadOnlyList<SurfaceActionBinding> currentBindings, IReadOnlyList<StoredActionBinding> descriptors, DateTimeOffset renewalThreshold)
    {
        if (currentBindings.Count > 0 &&
            currentBindings.All(binding => binding.BindingId is LegacyNewConversationBindingId or LegacyDeleteConversationBindingId))
            return true;
        var usable = currentBindings.Where(binding =>
                binding.Uses < binding.MaxUses &&
                binding.ExpiresAt > renewalThreshold &&
                binding.LastIdempotencyKey is null &&
                binding.LastOperationId is null)
            .ToArray();
        if (descriptors.Count == 0)
            return usable.Length > 0;
        if (usable.Length != descriptors.Count)
            return true;
        foreach (var descriptor in descriptors)
        {
            var binding = usable.FirstOrDefault(candidate =>
                string.Equals(candidate.BindingId, descriptor.BindingId, StringComparison.Ordinal));
            if (binding is null ||
                !string.Equals(binding.ActionType, descriptor.ActionType, StringComparison.Ordinal) ||
                !string.Equals(binding.InputSchemaRef, descriptor.InputSchemaRef, StringComparison.Ordinal) ||
                !string.Equals(binding.RequiredGrant, descriptor.RequiredGrant, StringComparison.Ordinal) ||
                binding.ActionSchemaVersion != descriptor.ActionSchemaVersion ||
                binding.MaxUses != descriptor.MaxUses)
                return true;
        }
        return false;
    }
    private static bool IsCanonicalConversationId(string? value)
    {
        if (value is null || value.Length != 68 || !value.StartsWith("ino-", StringComparison.Ordinal))
            return false;
        foreach (var character in value.AsSpan(4))
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        return true;
    }
    public static SurfaceFeedState Rebuild(SurfaceFeedState state, long expectedRevision, string projectionId, DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        DemandId(projectionId, nameof(projectionId));
        if (state.AppliedProjectionIds.Contains(projectionId, StringComparer.Ordinal)) return state;
        return ValidateAndCompact(state with
        {
            Revision = checked(state.Revision + 1),
            RebuildEpoch = checked(state.RebuildEpoch + 1),
            CurrentSurfaces = [],
            EventHistory = [],
            ActionBindings = [],
            AppliedProjectionIds = state.AppliedProjectionIds.Append(projectionId).ToArray()
        }, now);
    }
    public static void Validate(SurfaceFeedState state)
    {
        if (state.SchemaVersion != RuntimeStateSchemas.SurfaceFeed || state.Revision < 0 || state.LastSequence < 0 || state.RebuildEpoch < 0 || state.CurrentSurfaces is null || state.EventHistory is null || state.ActionBindings is null ||
            state.AppliedProjectionIds is null || state.DeliveryDedupe is null || state.Acknowledgements is null)
            throw new RuntimeStateIntegrityException("invalid surface-feed schema");
        if (state.Revision == 0 && state.Identity is not null || state.Revision > 0 && state.Identity is null)
            throw new RuntimeStateIntegrityException("invalid surface-feed identity lifecycle");
        if (state.Identity is not null) ValidateIdentity(state.Identity);
        if (state.CurrentSurfaces.Length > MaximumCurrentSurfaces || state.EventHistory.Length > MaximumEventHistory ||
            state.EventHistory.Sum(record => record.PayloadUtf8?.Length ?? 0) > MaximumEventHistoryPayloadBytes ||
            state.ActionBindings.Length > MaximumActionBindings ||
            state.AppliedProjectionIds.Length > MaximumProjectionDedupe || state.DeliveryDedupe.Length > MaximumDeliveryDedupe ||
            state.Acknowledgements.Length > MaximumAcknowledgements)
            throw new RuntimeStateIntegrityException("surface-feed retention bound exceeded");
        if (state.CurrentSurfaces.Select(surface => surface.SurfaceId).Distinct(StringComparer.Ordinal).Count() != state.CurrentSurfaces.Length ||
            state.ActionBindings.Select(binding => binding.BindingId).Distinct(StringComparer.Ordinal).Count() != state.ActionBindings.Length)
            throw new RuntimeStateIntegrityException("duplicate surface-feed identity");
        for (var index = 0; index < state.EventHistory.Length; index++)
        {
            var record = state.EventHistory[index];
            if (record.PayloadUtf8 is null || record.PayloadUtf8.Length > 64 * 1024 || record.Sequence < 1 ||
                index > 0 && state.EventHistory[index - 1].Sequence >= record.Sequence)
                throw new RuntimeStateIntegrityException("invalid surface-feed event history");
        }
        if (state.EventHistory.Length > 0 && state.EventHistory[^1].Sequence != state.LastSequence)
            throw new RuntimeStateIntegrityException("surface-feed history does not reach the latest sequence");
        foreach (var binding in state.ActionBindings) ValidateBinding(binding);
    }
    private static SurfaceFeedState ValidateAndCompact(SurfaceFeedState state, DateTimeOffset now)
    {
        state = state with
        {
            CurrentSurfaces = state.CurrentSurfaces.Where(surface => surface.ExpiresAt is null || surface.ExpiresAt > now)
                .OrderBy(surface => surface.Sequence).TakeLast(MaximumCurrentSurfaces).ToArray(),
            EventHistory = CompactHistory(state.EventHistory ?? []),
            AppliedProjectionIds = state.AppliedProjectionIds.TakeLast(MaximumProjectionDedupe).ToArray(),
            DeliveryDedupe = state.DeliveryDedupe.OrderBy(delivery => delivery.DeliveredAt).TakeLast(MaximumDeliveryDedupe).ToArray(),
            Acknowledgements = state.Acknowledgements.Where(cursor => cursor.ExpiresAt > now)
                .OrderBy(cursor => cursor.UpdatedAt).TakeLast(MaximumAcknowledgements).ToArray()
        };
        var currentKeys = state.CurrentSurfaces.Select(surface => (surface.SurfaceId, surface.SurfaceRevision)).ToHashSet();
        state = state with
        {
            ActionBindings = state.ActionBindings.Where(binding => currentKeys.Contains((binding.SurfaceId, binding.SurfaceRevision)) && binding.ExpiresAt > now)
                .TakeLast(MaximumActionBindings).ToArray()
        };
        Validate(state);
        return state;
    }
    private static SurfaceFeedRecord[] CompactHistory(IEnumerable<SurfaceFeedRecord> history)
    {
        var retained = history.OrderBy(record => record.Sequence)
            .TakeLast(MaximumEventHistory)
            .ToList();
        var bytes = retained.Sum(record => record.PayloadUtf8.Length);
        while (bytes > MaximumEventHistoryPayloadBytes && retained.Count > 0)
        {
            bytes -= retained[0].PayloadUtf8.Length;
            retained.RemoveAt(0);
        }
        return retained.ToArray();
    }
    private static void DemandMutable(SurfaceFeedState state, long expectedRevision)
    {
        DemandRevision(state, expectedRevision);
        if (state.Identity is null) throw new InvalidOperationException("Surface-feed state is not initialized.");
    }
    private static void DemandRevision(SurfaceFeedState state, long expectedRevision)
    {
        if (state.Revision != expectedRevision) throw new RuntimeStateConflictException(expectedRevision, state.Revision);
    }
    private static void ValidateIdentity(SurfaceFeedIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.OwnerId.Value) || string.IsNullOrWhiteSpace(identity.ActorId.Value))
            throw new ArgumentException("A complete surface-feed identity is required.", nameof(identity));
    }
    private static void ValidateProjection(SurfaceFeedProjection projection)
    {
        DemandId(projection.ProjectionId, nameof(projection.ProjectionId));
        DemandId(projection.SurfaceId, nameof(projection.SurfaceId));
        DemandHash(projection.ContentHash, nameof(projection.ContentHash));
        if (projection.SurfaceRevision < 1 || projection.PayloadUtf8 is null || projection.PayloadUtf8.Length > 64 * 1024 ||
            projection.ActionBindings is null || projection.ActionBindings.Length > MaximumActionBindings)
            throw new ArgumentException("Surface projections must be versioned and bounded.", nameof(projection));
        if (projection.ActionBindings.Select(binding => binding.BindingId).Distinct(StringComparer.Ordinal).Count() !=
            projection.ActionBindings.Length)
            throw new ArgumentException("Surface projection binding ids must be unique.", nameof(projection));
        foreach (var binding in projection.ActionBindings)
        {
            ValidateBinding(binding);
            if (!string.Equals(binding.SurfaceId, projection.SurfaceId, StringComparison.Ordinal) ||
                binding.SurfaceRevision != projection.SurfaceRevision || binding.ExpiresAt <= projection.CreatedAt)
                throw new ArgumentException("Action bindings must target the projected surface revision and remain unexpired.", nameof(projection));
        }
    }
    private static void ValidateHomeSurfaceBootstrap(SurfaceFeedState state, HomeSurfaceBootstrap bootstrap)
    {
        DemandId(bootstrap.ProjectionId, nameof(bootstrap.ProjectionId));
        DemandId(bootstrap.CorrelationId, nameof(bootstrap.CorrelationId));
        var identity = state.Identity ?? throw new RuntimeStateIntegrityException("surface-feed identity is missing");
        var expectedConversationId = "ino-" + RequestScope.Id(identity.OwnerId, identity.ActorId);
        if (!string.Equals(bootstrap.ConversationId, expectedConversationId, StringComparison.Ordinal))
            throw new ArgumentException("The home surface must target the feed identity's conversation.", nameof(bootstrap));
    }
    private static SurfaceActionBinding[] CreateHomeBindings(IReadOnlyList<StoredActionBinding> descriptors, int surfaceRevision) => descriptors.Select(descriptor => new SurfaceActionBinding(
            descriptor.BindingId,
            ConversationSurfacePayload.HomeSurfaceId,
            surfaceRevision,
            descriptor.ActionType,
            descriptor.InputSchemaRef,
            descriptor.RequiredGrant,
            descriptor.ActionSchemaVersion,
            Convert.ToHexStringLower(SHA256.HashData(RandomNumberGenerator.GetBytes(32))),
            descriptor.MaxUses,
            0,
            descriptor.ExpiresAt,
            null,
            null)).ToArray();
    private static void ValidateBinding(SurfaceActionBinding binding)
    {
        DemandId(binding.BindingId, nameof(binding.BindingId));
        DemandId(binding.SurfaceId, nameof(binding.SurfaceId));
        DemandId(binding.ActionType, nameof(binding.ActionType));
        DemandId(binding.InputSchemaRef, nameof(binding.InputSchemaRef));
        DemandId(binding.RequiredGrant, nameof(binding.RequiredGrant));
        DemandHash(binding.TokenHash, nameof(binding.TokenHash));
        if (binding.SurfaceRevision < 1 || binding.ActionSchemaVersion < 1 || binding.MaxUses < 1 || binding.Uses < 0 || binding.Uses > binding.MaxUses ||
            binding.LastIdempotencyKey is { Length: > 256 } || binding.LastOperationId is { Length: > 256 })
            throw new ArgumentException("Surface action binding is invalid.", nameof(binding));
    }
    private static bool FixedTimeHashEquals(string first, string second)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));
        }
        catch (FormatException)
        {
            return false;
        }
    }
    private static void DemandId(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("Surface-feed identifiers must be present and bounded.", name);
    }
    private static void DemandHash(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 digest is required.", name);
    }
}
