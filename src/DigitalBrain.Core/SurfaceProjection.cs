using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

public static class UiProtocol
{
    public const int ProtocolVersion = 2;
    public const string SurfaceSchema = "digitalbrain.surface";
    public const int SurfaceSchemaVersion = 2;
    public const int ActionSchemaVersion = 1;
    public static readonly TimeSpan ActionTokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SurfaceLifetime = TimeSpan.FromHours(24);
}

/// <summary>Projects the principal-private INO conversation into the stable authenticated surface slot.</summary>
public sealed class WorkspaceSurfaceProducer(
    IPrivateFeedStore feed,
    ActionExecutor actions,
    IInoConversationStore? conversations = null)
{
    public const int InoPayloadBudgetBytes = PrivateFeedStore.MaximumSurfacePayloadBytes - (2 * 1024);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SurfaceScopeKey, object> _scopeGates = new();
    public const string HomeSurfaceId = "workspace-home";
    public const string InoBindingId = "ino.send";
    public const string InoActionType = "ino.interact";
    public const string InoInputSchema = "digitalbrain.ino.prompt-input.v2";

    public StoredSurfaceRecord EnsureInitial(RequestContext context, SurfaceAudienceKind audienceKind = SurfaceAudienceKind.Principal)
    {
        lock (Gate(context, audienceKind))
        {
            using var mutation = actions.EnterSurfaceMutation();
            var conversation = Conversation(context);
            var record = feed.EnsureInitial(context, audienceKind, HomeSurfaceId,
                sequence => CreateRecord(context, audienceKind, sequence, revision: 1, "surface", "workspace-bootstrap", conversation));
            if ((record.ExpiresAt is { } surfaceExpiry && surfaceExpiry <= DateTimeOffset.UtcNow) ||
                (record.Actions.Count > 0 && record.Actions.All(static action => action.ExpiresAt <= DateTimeOffset.UtcNow)) ||
                (audienceKind == SurfaceAudienceKind.Principal &&
                 (!string.Equals(record.Payload.GetRawText(), BuildInoPayload(conversation).GetRawText(), StringComparison.Ordinal) ||
                  !HasCurrentInoActionPolicy(record, conversation))))
            {
                record = audienceKind == SurfaceAudienceKind.Principal
                    ? PublishInoConversationCore(context, conversation, "conversation-restore")
                    : PublishWorkspaceOverviewCore(context, "surface-policy-renewal", audienceKind);
                feed.RetainFrom(context, audienceKind, record.Sequence);
            }
            actions.NoteCurrentRevision(context, record.Audience, record.SurfaceId, record.Revision);
            return record;
        }
    }

    public StoredSurfaceRecord Republish(
        RequestContext context,
        string causeId,
        SurfaceAudienceKind audienceKind = SurfaceAudienceKind.Principal)
    {
        lock (Gate(context, audienceKind))
        {
            using var mutation = actions.EnterSurfaceMutation();
            return PublishWorkspaceOverviewCore(context, causeId, audienceKind);
        }
    }

    public StoredSurfaceRecord PublishInoConversation(
        RequestContext context,
        InoConversationSnapshot conversation)
    {
        if (!string.Equals(conversation.ConversationId, InoConversationIdentity.From(context), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The conversation projection is outside the authenticated scope.");
        lock (Gate(context, SurfaceAudienceKind.Principal))
        {
            using var mutation = actions.EnterSurfaceMutation();
            return PublishInoConversationCore(context, conversation, "ino-conversation");
        }
    }

    private StoredSurfaceRecord PublishWorkspaceOverviewCore(
        RequestContext context,
        string causeId,
        SurfaceAudienceKind audienceKind)
    {
        if (audienceKind == SurfaceAudienceKind.Principal)
            return PublishInoConversationCore(context, Conversation(context), causeId);
        var revision = checked((feed.LatestRevision(context, audienceKind, HomeSurfaceId) ?? 0) + 1);
        var now = DateTimeOffset.UtcNow;
        var payload = BuildWorkspacePayload();
        IReadOnlyList<StoredActionBinding> descriptors = [];
        var contentHash = SurfaceContentHash.Compute(payload, descriptors);
        var record = feed.Append(
            context,
            audienceKind,
            HomeSurfaceId,
            revision,
            contentHash,
            now,
            now.Add(UiProtocol.SurfaceLifetime),
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

    private StoredSurfaceRecord PublishInoConversationCore(
        RequestContext context,
        InoConversationSnapshot conversation,
        string causeId)
    {
        var audienceKind = SurfaceAudienceKind.Principal;
        var revision = checked((feed.LatestRevision(context, audienceKind, HomeSurfaceId) ?? 0) + 1);
        var now = DateTimeOffset.UtcNow;
        var payload = BuildInoPayload(conversation);
        var descriptors = BuildInoActions(now, conversation);
        var record = feed.Append(
            context,
            audienceKind,
            HomeSurfaceId,
            revision,
            SurfaceContentHash.Compute(payload, descriptors),
            now,
            now.Add(UiProtocol.SurfaceLifetime),
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

    private object Gate(RequestContext context, SurfaceAudienceKind audienceKind)
    {
        var audience = PrivateFeedStore.Audience(context, audienceKind);
        var key = new SurfaceScopeKey(context.TenantId, context.WorkspaceId, context.Principal, audienceKind, audience.Id);
        return _scopeGates.GetOrAdd(key, static _ => new object());
    }

    private static StoredSurfaceRecord CreateRecord(
        RequestContext context,
        SurfaceAudienceKind audienceKind,
        long sequence,
        int revision,
        string causeKind,
        string causeId,
        InoConversationSnapshot conversation)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = audienceKind == SurfaceAudienceKind.Principal
            ? BuildInoPayload(conversation)
            : BuildWorkspacePayload();
        var descriptors = audienceKind == SurfaceAudienceKind.Principal
            ? BuildInoActions(now, conversation)
            : [];
        return new(
            sequence,
            context.TenantId,
            context.WorkspaceId,
            PrivateFeedStore.Audience(context, audienceKind),
            HomeSurfaceId,
            revision,
            SurfaceContentHash.Compute(payload, descriptors),
            now,
            now.Add(UiProtocol.SurfaceLifetime),
            context.CorrelationId,
            causeKind,
            causeId,
            audienceKind == SurfaceAudienceKind.Principal ? InoRequiredCapabilities : WorkspaceRequiredCapabilities,
            payload,
            descriptors,
            AudiencePrincipalKind: audienceKind == SurfaceAudienceKind.Principal ? context.Principal.Kind : null);
    }

    public static JsonElement BuildInoPayload(InoConversationSnapshot conversation)
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

    private static string BuildTurnKey(InoConversationTurn turn)
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

    private static IReadOnlyList<StoredActionBinding> BuildInoActions(
        DateTimeOffset now,
        InoConversationSnapshot conversation) =>
        conversation.CurrentOperation is { } operation && InoConversationStates.IsActive(operation.State)
            ? []
            : [new(InoBindingId, InoActionType, InoInputSchema, "ui.action", 1, now.Add(UiProtocol.SurfaceLifetime))];

    private static bool HasCurrentInoActionPolicy(
        StoredSurfaceRecord record,
        InoConversationSnapshot conversation)
    {
        if (conversation.CurrentOperation is { } operation && InoConversationStates.IsActive(operation.State))
            return record.Actions.Count == 0;
        if (record.Actions.Count != 1) return false;
        var action = record.Actions[0];
        return string.Equals(action.BindingId, InoBindingId, StringComparison.Ordinal) &&
               string.Equals(action.ActionType, InoActionType, StringComparison.Ordinal) &&
               string.Equals(action.InputSchemaRef, InoInputSchema, StringComparison.Ordinal) &&
               string.Equals(action.RequiredGrant, "ui.action", StringComparison.Ordinal) &&
               action.MaxUses == 1 &&
               action.ActionSchemaVersion == UiProtocol.ActionSchemaVersion &&
               action.ExpiresAt > DateTimeOffset.UtcNow;
    }

    private InoConversationSnapshot Conversation(RequestContext context) =>
        conversations?.Read(context) ?? InoConversationSnapshot.Empty(context);

    private static readonly string[] InoRequiredCapabilities =
        ["ui.protocol.v2", "ui.payload.native", "ui.native.ino-conversation", "ui.native.typed-actions"];
    private static readonly string[] WorkspaceRequiredCapabilities = ["ui.protocol.v2", "ui.payload.native"];

    private readonly record struct SurfaceScopeKey(
        TenantId Tenant,
        WorkspaceId Workspace,
        PrincipalRef Principal,
        SurfaceAudienceKind AudienceKind,
        string AudienceId);
}

public static class SurfaceContentHash
{
    public static string Compute(JsonElement payload, IReadOnlyList<StoredActionBinding> actions)
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
public sealed class SurfaceEnvelopeWriter(ActionExecutor actions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Write(RequestContext recipient, StoredSurfaceRecord record, IReadOnlySet<string> clientCapabilities)
    {
        DemandVisible(recipient, record);
        if (record.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("An expired surface cannot be materialized.");
        if (record.ProtocolVersion != UiProtocol.ProtocolVersion || record.SurfaceSchemaVersion != UiProtocol.SurfaceSchemaVersion ||
            record.ActionSchemaVersion != UiProtocol.ActionSchemaVersion ||
            !string.Equals(record.SurfaceSchema, UiProtocol.SurfaceSchema, StringComparison.Ordinal))
            throw new InvalidOperationException("The stored surface protocol metadata is unsupported.");
        try { SurfacePayloadPolicy.DemandSafe(record.Payload); }
        catch (ArgumentException exception) { throw new InvalidOperationException("The stored surface payload violates presentation policy.", exception); }
        var missing = record.RequiredClientCapabilities.Where(required => !clientCapabilities.Contains(required)).ToArray();
        if (missing.Length > 0) throw new SurfaceCapabilityException(missing);

        var wireActions = new List<object>();
        if (record.Audience.Kind == SurfaceAudienceKind.Principal)
            foreach (var binding in record.Actions)
            {
                try
                {
                    var issued = actions.Issue(recipient, record, binding, UiProtocol.ActionTokenLifetime);
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
                catch (ActionRejectedException exception) when (exception.Reason is ActionRejection.Expired or ActionRejection.Replay)
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
