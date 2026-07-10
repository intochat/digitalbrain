using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

public static class V2UiProtocol
{
    public const int ProtocolVersion = 2;
    public const string SurfaceSchema = "digitalbrain.surface";
    public const int SurfaceSchemaVersion = 2;
    public const int ActionSchemaVersion = 1;
    public static readonly TimeSpan ActionTokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SurfaceLifetime = TimeSpan.FromHours(24);
}

/// <summary>Projects the principal-private INO conversation into the stable authenticated surface slot.</summary>
public sealed class V2WorkspaceSurfaceProducer(
    IV2PrivateFeedStore feed,
    V2ActionExecutor actions,
    IV2InoConversationStore? conversations = null)
{
    public const int InoPayloadBudgetBytes = V2PrivateFeedStore.MaximumSurfacePayloadBytes - (2 * 1024);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SurfaceScopeKey, object> _scopeGates = new();
    public const string HomeSurfaceId = "workspace-home";
    public const string InoBindingId = "ino.send";
    public const string InoActionType = "ino.interact";
    public const string InoInputSchema = "digitalbrain.ino.prompt-input.v1";

    public V2StoredSurfaceRecord EnsureInitial(RequestContext context, V2SurfaceAudienceKind audienceKind = V2SurfaceAudienceKind.Principal)
    {
        lock (Gate(context, audienceKind))
        {
            using var mutation = actions.EnterSurfaceMutation();
            var conversation = Conversation(context);
            var record = feed.EnsureInitial(context, audienceKind, HomeSurfaceId,
                sequence => CreateRecord(context, audienceKind, sequence, revision: 1, "surface", "workspace-bootstrap", conversation));
            if ((record.ExpiresAt is { } surfaceExpiry && surfaceExpiry <= DateTimeOffset.UtcNow) ||
                (record.Actions.Count > 0 && record.Actions.All(static action => action.ExpiresAt <= DateTimeOffset.UtcNow)) ||
                (audienceKind == V2SurfaceAudienceKind.Principal &&
                 !string.Equals(record.Payload.GetRawText(), BuildInoPayload(conversation).GetRawText(), StringComparison.Ordinal)))
            {
                record = audienceKind == V2SurfaceAudienceKind.Principal
                    ? PublishInoConversationCore(context, conversation, "conversation-restore")
                    : PublishWorkspaceOverviewCore(context, "surface-policy-renewal", audienceKind);
                feed.RetainFrom(context, audienceKind, record.Sequence);
            }
            actions.NoteCurrentRevision(context, record.Audience, record.SurfaceId, record.Revision);
            return record;
        }
    }

    public V2StoredSurfaceRecord Republish(
        RequestContext context,
        string causeId,
        V2SurfaceAudienceKind audienceKind = V2SurfaceAudienceKind.Principal)
    {
        lock (Gate(context, audienceKind))
        {
            using var mutation = actions.EnterSurfaceMutation();
            return PublishWorkspaceOverviewCore(context, causeId, audienceKind);
        }
    }

    public V2StoredSurfaceRecord PublishInoConversation(
        RequestContext context,
        V2InoConversationSnapshot conversation)
    {
        if (!string.Equals(conversation.ConversationId, V2InoConversationIdentity.From(context), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The conversation projection is outside the authenticated scope.");
        lock (Gate(context, V2SurfaceAudienceKind.Principal))
        {
            using var mutation = actions.EnterSurfaceMutation();
            return PublishInoConversationCore(context, conversation, "ino-conversation");
        }
    }

    private V2StoredSurfaceRecord PublishWorkspaceOverviewCore(
        RequestContext context,
        string causeId,
        V2SurfaceAudienceKind audienceKind)
    {
        if (audienceKind == V2SurfaceAudienceKind.Principal)
            return PublishInoConversationCore(context, Conversation(context), causeId);
        var revision = checked((feed.LatestRevision(context, audienceKind, HomeSurfaceId) ?? 0) + 1);
        var now = DateTimeOffset.UtcNow;
        var payload = BuildWorkspacePayload();
        IReadOnlyList<V2StoredActionBinding> descriptors = [];
        var contentHash = V2SurfaceContentHash.Compute(payload, descriptors);
        var record = feed.Append(
            context,
            audienceKind,
            HomeSurfaceId,
            revision,
            contentHash,
            now,
            now.Add(V2UiProtocol.SurfaceLifetime),
            context.CorrelationId,
            "command",
            causeId,
            WorkspaceRequiredCapabilities,
            payload,
            descriptors);
        feed.RetainFrom(context, audienceKind, record.Sequence);
        actions.NoteCurrentRevision(context, record.Audience, record.SurfaceId, record.Revision);
        return record;
    }

    private V2StoredSurfaceRecord PublishInoConversationCore(
        RequestContext context,
        V2InoConversationSnapshot conversation,
        string causeId)
    {
        var audienceKind = V2SurfaceAudienceKind.Principal;
        var revision = checked((feed.LatestRevision(context, audienceKind, HomeSurfaceId) ?? 0) + 1);
        var now = DateTimeOffset.UtcNow;
        var payload = BuildInoPayload(conversation);
        var descriptors = BuildInoActions(now, conversation);
        var record = feed.Append(
            context,
            audienceKind,
            HomeSurfaceId,
            revision,
            V2SurfaceContentHash.Compute(payload, descriptors),
            now,
            now.Add(V2UiProtocol.SurfaceLifetime),
            context.CorrelationId,
            "command",
            causeId,
            InoRequiredCapabilities,
            payload,
            descriptors);
        // Keep the complete ordered lifecycle even when model execution finishes before the client pulls.
        feed.RetainFrom(context, audienceKind, Math.Max(1, record.Sequence - 3));
        actions.NoteCurrentRevision(context, record.Audience, record.SurfaceId, record.Revision);
        return record;
    }

    private object Gate(RequestContext context, V2SurfaceAudienceKind audienceKind)
    {
        var audience = V2PrivateFeedStore.Audience(context, audienceKind);
        var key = new SurfaceScopeKey(context.TenantId, context.WorkspaceId, context.Principal, audienceKind, audience.Id);
        return _scopeGates.GetOrAdd(key, static _ => new object());
    }

    private static V2StoredSurfaceRecord CreateRecord(
        RequestContext context,
        V2SurfaceAudienceKind audienceKind,
        long sequence,
        int revision,
        string causeKind,
        string causeId,
        V2InoConversationSnapshot conversation)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = audienceKind == V2SurfaceAudienceKind.Principal
            ? BuildInoPayload(conversation)
            : BuildWorkspacePayload();
        var descriptors = audienceKind == V2SurfaceAudienceKind.Principal
            ? BuildInoActions(now, conversation)
            : [];
        return new(
            sequence,
            context.TenantId,
            context.WorkspaceId,
            V2PrivateFeedStore.Audience(context, audienceKind),
            HomeSurfaceId,
            revision,
            V2SurfaceContentHash.Compute(payload, descriptors),
            now,
            now.Add(V2UiProtocol.SurfaceLifetime),
            context.CorrelationId,
            causeKind,
            causeId,
            audienceKind == V2SurfaceAudienceKind.Principal ? InoRequiredCapabilities : WorkspaceRequiredCapabilities,
            payload,
            descriptors,
            AudiencePrincipalKind: audienceKind == V2SurfaceAudienceKind.Principal ? context.Principal.Kind : null);
    }

    public static JsonElement BuildInoPayload(V2InoConversationSnapshot conversation)
    {
        var current = conversation.CurrentOperation;
        Dictionary<string, object?>? operation = current is null
            ? null
            : new Dictionary<string, object?>
            {
                ["state"] = current.State,
                ["retryable"] = current.Retryable
            };
        if (operation is not null && !string.IsNullOrWhiteSpace(current!.SafeReason))
            operation["safeReason"] = current.SafeReason;
        if (operation is not null && current!.Action is { } action)
        {
            operation["action"] = new Dictionary<string, object?>
            {
                ["kind"] = action.Kind,
                ["label"] = action.Label,
                ["target"] = action.Target
            };
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "native",
            nativeKind = "inoConversation",
            data = new
            {
                intro = "Ask INO about this workspace. I can help you understand what’s here and decide what to do next.",
                messages = conversation.Turns.Select(static turn => new
                {
                    turnKey = BuildTurnKey(turn),
                    role = turn.Role,
                    text = turn.Text,
                    state = turn.State
                }).ToArray(),
                operation
            }
        });
    }

    private static string BuildTurnKey(V2InoConversationTurn turn)
    {
        var source = Encoding.UTF8.GetBytes(turn.CommandId + "\0" + turn.Role);
        var hash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        return "turn-" + hash[..24];
    }

    private static JsonElement BuildWorkspacePayload() => JsonSerializer.SerializeToElement(new
    {
        kind = "native",
        nativeKind = "workspaceOverview",
        data = new { intro = "Your workspace is ready." }
    });

    private static IReadOnlyList<V2StoredActionBinding> BuildInoActions(
        DateTimeOffset now,
        V2InoConversationSnapshot conversation) =>
        conversation.CurrentOperation is { } operation && V2InoConversationStates.IsActive(operation.State)
            ? []
            : [new(InoBindingId, InoActionType, InoInputSchema, "ui.action", 1, now.Add(V2UiProtocol.SurfaceLifetime))];

    private V2InoConversationSnapshot Conversation(RequestContext context) =>
        conversations?.Read(context) ?? V2InoConversationSnapshot.Empty(context);

    private static readonly string[] InoRequiredCapabilities =
        ["ui.protocol.v2", "ui.payload.native", "ui.native.ino-conversation", "ui.native.typed-actions"];
    private static readonly string[] WorkspaceRequiredCapabilities = ["ui.protocol.v2", "ui.payload.native"];

    private readonly record struct SurfaceScopeKey(
        TenantId Tenant,
        WorkspaceId Workspace,
        PrincipalRef Principal,
        V2SurfaceAudienceKind AudienceKind,
        string AudienceId);
}

public static class V2SurfaceContentHash
{
    public static string Compute(JsonElement payload, IReadOnlyList<V2StoredActionBinding> actions)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            payload,
            actions = actions.Select(static action => new
            {
                action.BindingId,
                action.ActionType,
                action.InputSchemaRef,
                action.RequiredGrant,
                action.MaxUses,
                action.ExpiresAt,
                action.ActionSchemaVersion
            })
        });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

/// <summary>Converts a token-free stored record into the Flutter SurfaceEnvelope JSON wire contract.</summary>
public sealed class V2SurfaceEnvelopeWriter(V2ActionExecutor actions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Write(RequestContext recipient, V2StoredSurfaceRecord record, IReadOnlySet<string> clientCapabilities)
    {
        DemandVisible(recipient, record);
        if (record.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("An expired V2 surface cannot be materialized.");
        if (record.ProtocolVersion != V2UiProtocol.ProtocolVersion || record.SurfaceSchemaVersion != V2UiProtocol.SurfaceSchemaVersion ||
            record.ActionSchemaVersion != V2UiProtocol.ActionSchemaVersion ||
            !string.Equals(record.SurfaceSchema, V2UiProtocol.SurfaceSchema, StringComparison.Ordinal))
            throw new InvalidOperationException("The stored V2 surface protocol metadata is unsupported.");
        try { V2SurfacePayloadPolicy.DemandSafe(record.Payload); }
        catch (ArgumentException exception) { throw new InvalidOperationException("The stored V2 surface payload violates presentation policy.", exception); }
        var missing = record.RequiredClientCapabilities.Where(required => !clientCapabilities.Contains(required)).ToArray();
        if (missing.Length > 0) throw new V2SurfaceCapabilityException(missing);

        var wireActions = new List<object>();
        if (record.Audience.Kind == V2SurfaceAudienceKind.Principal)
            foreach (var binding in record.Actions)
            {
                try
                {
                    var issued = actions.Issue(recipient, record, binding, V2UiProtocol.ActionTokenLifetime);
                    wireActions.Add(new
                    {
                        actionSchemaVersion = binding.ActionSchemaVersion,
                        bindingId = binding.BindingId,
                        actionType = binding.ActionType,
                        actionToken = issued.Token,
                        surfaceId = record.SurfaceId,
                        surfaceRevision = record.Revision,
                        expiresAt = issued.ExpiresAt.ToUniversalTime().ToString("O")
                    });
                }
                catch (V2ActionRejectedException exception) when (exception.Reason is V2ActionRejection.Expired or V2ActionRejection.Replay)
                {
                    // The surface remains renderable; an expired/consumed binding is simply not reissued.
                }
            }

        var envelope = new
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
        };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private static void DemandVisible(RequestContext recipient, V2StoredSurfaceRecord record)
    {
        if (record.TenantId != recipient.TenantId || record.WorkspaceId != recipient.WorkspaceId)
            throw new UnauthorizedAccessException("V2 surface scope denied.");
        var visible = record.Audience.Kind switch
        {
            V2SurfaceAudienceKind.Principal =>
                record.AudiencePrincipalKind == recipient.Principal.Kind &&
                string.Equals(record.Audience.Id, V2PrincipalScope.Id(recipient.Principal), StringComparison.Ordinal),
            V2SurfaceAudienceKind.Workspace => string.Equals(record.Audience.Id, recipient.WorkspaceId.Value, StringComparison.Ordinal),
            V2SurfaceAudienceKind.Public => string.IsNullOrEmpty(record.Audience.Id),
            _ => false
        };
        if (!visible) throw new UnauthorizedAccessException("V2 surface audience denied.");
    }

}

public sealed class V2SurfaceCapabilityException(IReadOnlyList<string> missing)
    : InvalidOperationException("The V2 client does not support required surface capabilities.")
{
    public IReadOnlyList<string> Missing { get; } = missing;
}
