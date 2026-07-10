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

/// <summary>Creates the real initial workspace surface and projects refresh actions into new revisions.</summary>
public sealed class V2WorkspaceSurfaceProducer(IV2PrivateFeedStore feed, V2ActionExecutor actions)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SurfaceScopeKey, object> _scopeGates = new();
    public const string HomeSurfaceId = "workspace-home";
    public const string RefreshBindingId = "workspace.refresh";
    public const string RefreshActionType = "ui.surface.refresh";

    public V2StoredSurfaceRecord EnsureInitial(RequestContext context, V2SurfaceAudienceKind audienceKind = V2SurfaceAudienceKind.Principal)
    {
        lock (Gate(context, audienceKind))
        {
            using var mutation = actions.EnterSurfaceMutation();
            var record = feed.EnsureInitial(context, audienceKind, HomeSurfaceId,
                sequence => CreateRecord(context, audienceKind, sequence, revision: 1, "surface", "workspace-bootstrap"));
            if ((record.ExpiresAt is { } surfaceExpiry && surfaceExpiry <= DateTimeOffset.UtcNow) ||
                (record.Actions.Count > 0 && record.Actions.All(static action => action.ExpiresAt <= DateTimeOffset.UtcNow)))
            {
                record = PublishRefreshCore(context, "surface-policy-renewal", audienceKind);
            }
            actions.NoteCurrentRevision(context, record.Audience, record.SurfaceId, record.Revision);
            return record;
        }
    }

    public V2StoredSurfaceRecord PublishRefresh(
        RequestContext context,
        string causeId,
        V2SurfaceAudienceKind audienceKind = V2SurfaceAudienceKind.Principal)
    {
        lock (Gate(context, audienceKind))
        {
            using var mutation = actions.EnterSurfaceMutation();
            return PublishRefreshCore(context, causeId, audienceKind);
        }
    }

    private V2StoredSurfaceRecord PublishRefreshCore(
        RequestContext context,
        string causeId,
        V2SurfaceAudienceKind audienceKind)
    {
        var revision = checked((feed.LatestRevision(context, audienceKind, HomeSurfaceId) ?? 0) + 1);
        var now = DateTimeOffset.UtcNow;
        var payload = BuildPayload(revision, audienceKind);
        var descriptors = BuildActions(now, audienceKind);
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
            RequiredCapabilities,
            payload,
            descriptors);
        feed.RetainFrom(context, audienceKind, record.Sequence);
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
        string causeId)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = BuildPayload(revision, audienceKind);
        var descriptors = BuildActions(now, audienceKind);
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
            RequiredCapabilities,
            payload,
            descriptors,
            AudiencePrincipalKind: audienceKind == V2SurfaceAudienceKind.Principal ? context.Principal.Kind : null);
    }

    private static JsonElement BuildPayload(int revision, V2SurfaceAudienceKind audienceKind)
    {
        var children = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["Type"] = "text",
                ["Props"] = new Dictionary<string, object?>
                {
                    ["text"] = revision == 1
                        ? "Your private V2 workspace feed is connected."
                        : "Workspace surface refreshed successfully."
                }
            }
        };
        if (audienceKind == V2SurfaceAudienceKind.Principal)
        {
            children.Add(new Dictionary<string, object?>
            {
                ["Type"] = "forui:fbutton",
                ["Props"] = new Dictionary<string, object?>
                {
                    ["label"] = "Refresh workspace",
                    ["actionBindingId"] = RefreshBindingId
                }
            });
        }

        return JsonSerializer.SerializeToElement(new
        {
            kind = "widgetTree",
            tree = new Dictionary<string, object?>
            {
                ["Type"] = "forui:fcard",
                ["Props"] = new Dictionary<string, object?>
                {
                    ["title"] = "DigitalBrain Runtime V2",
                    ["subtitle"] = "Authenticated workspace surface"
                },
                ["Children"] = children
            },
            data = new Dictionary<string, object?>
            {
                ["kind"] = "v2-workspace-home",
                ["status"] = "ready",
                ["revision"] = revision
            }
        });
    }

    private static IReadOnlyList<V2StoredActionBinding> BuildActions(DateTimeOffset now, V2SurfaceAudienceKind audienceKind) =>
        audienceKind == V2SurfaceAudienceKind.Principal
            ? [new(RefreshBindingId, RefreshActionType, "digitalbrain.ui.refresh-input.v1", "ui.action", 1, now.Add(V2UiProtocol.SurfaceLifetime))]
            : [];

    private static readonly string[] RequiredCapabilities =
        ["ui.protocol.v2", "ui.payload.widgetTree", "ui.widget-vocabulary.v2", "ui.native.typed-actions"];

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

/// <summary>The only live UI command: a narrowly-bound refresh projection, not arbitrary brain.act admission.</summary>
public sealed class V2SurfaceRefreshCommandHandler(V2WorkspaceSurfaceProducer producer) : IV2CommandHandler
{
    public bool CanHandle(string commandType) => string.Equals(commandType, V2WorkspaceSurfaceProducer.RefreshActionType, StringComparison.Ordinal);

    public Task<V2CommandExecutionResult> ExecuteAsync(V2CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        producer.PublishRefresh(command.Context, command.CommandId);
        return Task.FromResult(V2CommandExecutionResult.Success());
    }
}
