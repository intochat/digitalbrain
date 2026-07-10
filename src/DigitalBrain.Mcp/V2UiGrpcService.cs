using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core.V2;
using DigitalBrain.V2.Ui.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record V2UiBootstrapOptions(
    string Secret,
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    PrincipalRef Principal,
    TimeSpan AccessLifetime,
    IReadOnlySet<string> Grants)
{
    public static V2UiBootstrapOptions FromConfiguration(IConfiguration configuration)
    {
        var secret = configuration["DigitalBrain:V2:Ui:BootstrapSecret"] ?? string.Empty;
        var tenant = configuration["DigitalBrain:V2:Ui:TenantId"] ?? "local";
        var workspace = configuration["DigitalBrain:V2:Ui:WorkspaceId"] ?? "default";
        var principal = configuration["DigitalBrain:V2:Ui:PrincipalId"] ?? "flutter-ui";
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(principal) ||
            tenant.Length > 256 || workspace.Length > 256 || principal.Length > 256)
            throw new InvalidOperationException("V2 UI bootstrap identity configuration must be complete.");
        return new(secret, new(tenant), new(workspace), new(principal, PrincipalKind.User), TimeSpan.FromMinutes(15),
            new HashSet<string>(StringComparer.Ordinal) { "brain.read", "ui.action", "gmail.read" });
    }
}

public sealed record V2UiDeliveryOptions(
    TimeSpan ActionTokenRenewalInterval,
    TimeSpan AuthenticationRevalidationInterval)
{
    public static V2UiDeliveryOptions Default { get; } = new(TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(5));

    public V2UiDeliveryOptions Validate()
    {
        if (ActionTokenRenewalInterval <= TimeSpan.Zero || ActionTokenRenewalInterval >= V2UiProtocol.ActionTokenLifetime)
            throw new InvalidOperationException("V2 UI action-token renewal must be positive and shorter than the action-token lifetime.");
        if (AuthenticationRevalidationInterval <= TimeSpan.Zero || AuthenticationRevalidationInterval > TimeSpan.FromMinutes(1))
            throw new InvalidOperationException("V2 UI authentication revalidation must be between zero and one minute.");
        return this;
    }
}

public sealed class V2UiBootstrapAuthenticator(V2UiBootstrapOptions options, IV2SessionManager sessions)
{
    public bool TryBootstrap(string suppliedSecret, out V2RequestContext context, out V2SessionPair pair)
    {
        context = default!;
        pair = default!;
        if (string.IsNullOrEmpty(options.Secret) || string.IsNullOrEmpty(suppliedSecret) ||
            !FixedTimeEquals(options.Secret, suppliedSecret)) return false;
        context = new V2RequestContext(
            options.TenantId,
            options.WorkspaceId,
            options.Principal,
            $"v2-ui-session-{Guid.NewGuid():N}",
            AuthAssurance.Password,
            Guid.NewGuid().ToString("N"),
            null,
            options.Grants);
        pair = sessions.Create(context, options.AccessLifetime, V2SessionAudiences.Ui);
        return true;
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}

public sealed class V2UiGrpcService(
    V2UiBootstrapAuthenticator bootstrap,
    IV2SessionManager sessions,
    V2SessionTokenService tokens,
    IV2PrivateFeedStore feed,
    V2WorkspaceSurfaceProducer producer,
    V2SurfaceEnvelopeWriter envelopeWriter,
    V2ActionExecutor actionExecutor,
    V2ApplicationService application,
    V2UiDeliveryOptions deliveryOptions,
    ILogger<V2UiGrpcService> logger) : DigitalBrainV2Ui.DigitalBrainV2UiBase
{
    private const int MaximumInputBytes = 64 * 1024;
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Mcp");

    public override Task<SessionReply> BootstrapSession(BootstrapSessionRequest request, ServerCallContext context)
    {
        DemandAudience(context);
        if (!bootstrap.TryBootstrap(request.Secret, out var authenticated, out var pair))
        {
            logger.LogWarning("V2 UI bootstrap was denied.");
            throw Unauthenticated();
        }
        using var activity = ActivitySource.StartActivity("v2.ui.session.bootstrap", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.outcome", "success");
        logger.LogInformation("V2 UI bootstrap issued a workspace-scoped session.");
        return Task.FromResult(ToReply(authenticated, pair));
    }

    public override Task<SessionReply> RefreshSession(RefreshSessionRequest request, ServerCallContext context)
    {
        DemandAudience(context);
        if (string.IsNullOrWhiteSpace(request.RefreshToken) ||
            !sessions.TryRefresh(request.RefreshToken, TimeSpan.FromMinutes(15), V2SessionAudiences.Ui, out var pair) ||
            !tokens.TryValidate(pair.AccessToken, V2SessionAudiences.Ui, out var authenticated))
        {
            logger.LogWarning("V2 UI session refresh was denied.");
            throw Unauthenticated();
        }
        using var activity = ActivitySource.StartActivity("v2.ui.session.refresh", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.outcome", "success");
        return Task.FromResult(ToReply(authenticated, pair));
    }

    public override async Task WatchSurfaceFeed(
        WatchSurfaceFeedRequest request,
        IServerStreamWriter<SurfaceFeedEvent> responseStream,
        ServerCallContext context)
    {
        var session = Authenticate(context);
        var authenticated = session.Context;
        if (request.AfterSequence < 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "after_sequence cannot be negative."));
        var audienceKind = AudienceKind(request.Audience);
        var capabilities = ValidateCapabilities(request.ClientCapabilities);
        var batchSize = Math.Clamp(request.MaxBatchSize <= 0 ? 50 : request.MaxBatchSize, 1, 100);
        producer.EnsureInitial(authenticated, audienceKind);

        logger.LogInformation("V2 UI feed opened for {AudienceKind} audience.", audienceKind);
        var cursor = request.AfterSequence;
        var delivered = false;
        var firstRead = true;
        var rematerializeAfterResume = request.AfterSequence > 0;
        var forceSnapshotReason = string.Empty;
        var nextActionRenewal = DateTimeOffset.UtcNow.Add(deliveryOptions.ActionTokenRenewalInterval);
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var leaseRemaining = session.ExpiresAt - DateTimeOffset.UtcNow;
        if (leaseRemaining <= TimeSpan.Zero) throw Unauthenticated();
        leaseCancellation.CancelAfter(leaseRemaining);
        try
        {
            while (!leaseCancellation.IsCancellationRequested)
            {
                Revalidate(session);
                producer.EnsureInitial(authenticated, audienceKind);
                var page = string.IsNullOrEmpty(forceSnapshotReason)
                    ? feed.CatchUp(authenticated, audienceKind, cursor, batchSize)
                    : feed.CatchUp(authenticated, audienceKind, long.MaxValue, batchSize);
                if (firstRead && request.AfterSequence > 0 && !page.ResetRequired && page.Items.Count == 0 &&
                    page.LatestSequence == request.AfterSequence)
                {
                    forceSnapshotReason = "reconnect-token-rematerialization";
                    rematerializeAfterResume = false;
                    page = feed.CatchUp(authenticated, audienceKind, long.MaxValue, batchSize);
                }
                firstRead = false;
                if (page.ResetRequired)
                {
                    var reset = new SurfaceFeedReset
                    {
                        Reason = string.IsNullOrEmpty(forceSnapshotReason) ? "sequence-retention-gap" : forceSnapshotReason,
                        ResumeSequence = page.LatestSequence
                    };
                    foreach (var item in page.Items)
                        reset.SnapshotJson.Add(Materialize(authenticated, item, capabilities));
                    Revalidate(session);
                    await responseStream.WriteAsync(new SurfaceFeedEvent { Reset = reset }).ConfigureAwait(false);
                    feed.MarkDelivered(authenticated, audienceKind, page.LatestSequence);
                    cursor = page.LatestSequence;
                    RecordDelivery("reset", audienceKind, reset.SnapshotJson.Count);
                    delivered = true;
                    rematerializeAfterResume = false;
                    forceSnapshotReason = string.Empty;
                    nextActionRenewal = DateTimeOffset.UtcNow.Add(deliveryOptions.ActionTokenRenewalInterval);
                    continue;
                }

                foreach (var item in page.Items)
                {
                    leaseCancellation.Token.ThrowIfCancellationRequested();
                    Revalidate(session);
                    var feedEvent = new SurfaceFeedEvent
                    {
                        SurfaceJson = Materialize(authenticated, item, capabilities)
                    };
                    await responseStream.WriteAsync(feedEvent).ConfigureAwait(false);
                    feed.MarkDelivered(authenticated, audienceKind, item.Sequence);
                    cursor = item.Sequence;
                    if (!delivered)
                    {
                        RecordDelivery("surface", audienceKind, 1);
                        delivered = true;
                    }
                    nextActionRenewal = DateTimeOffset.UtcNow.Add(deliveryOptions.ActionTokenRenewalInterval);
                }
                if (page.Next is not null) continue;
                if (rematerializeAfterResume)
                {
                    rematerializeAfterResume = false;
                    forceSnapshotReason = "reconnect-token-rematerialization";
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var revalidateAfter = deliveryOptions.AuthenticationRevalidationInterval;
                var renewAfter = audienceKind == V2SurfaceAudienceKind.Principal
                    ? nextActionRenewal - now
                    : TimeSpan.MaxValue;
                var wakeAfter = renewAfter < revalidateAfter ? renewAfter : revalidateAfter;
                if (wakeAfter <= TimeSpan.Zero)
                {
                    forceSnapshotReason = "action-token-renewal";
                    continue;
                }
                using var wakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(leaseCancellation.Token);
                wakeCancellation.CancelAfter(wakeAfter);
                try
                {
                    await feed.WaitForChangeAsync(authenticated, audienceKind, cursor, wakeCancellation.Token).ConfigureAwait(false);
                    Revalidate(session);
                }
                catch (OperationCanceledException) when (!leaseCancellation.IsCancellationRequested)
                {
                    Revalidate(session);
                    if (audienceKind == V2SurfaceAudienceKind.Principal && DateTimeOffset.UtcNow >= nextActionRenewal)
                        forceSnapshotReason = "action-token-renewal";
                }
            }
            if (!context.CancellationToken.IsCancellationRequested) throw Unauthenticated();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Normal gRPC stream cancellation; no error-level log and no sensitive peer/session details.
        }
        catch (OperationCanceledException)
        {
            throw Unauthenticated();
        }
        finally
        {
            logger.LogInformation("V2 UI feed closed for {AudienceKind} audience after delivery={Delivered}.", audienceKind, delivered);
        }
    }

    public override async Task<AcknowledgeSurfaceFeedReply> AcknowledgeSurfaceFeed(AcknowledgeSurfaceFeedRequest request, ServerCallContext context)
    {
        var authenticated = Authenticate(context).Context;
        if (request.Sequence < 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "sequence cannot be negative."));
        var audienceKind = AudienceKind(request.Audience);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        wait.CancelAfter(TimeSpan.FromMilliseconds(250));
        if (!await feed.WaitUntilDeliveredAsync(authenticated, audienceKind, request.Sequence, wait.Token).ConfigureAwait(false))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The feed sequence has not been delivered."));
        try { feed.Acknowledge(authenticated, audienceKind, request.Sequence); }
        catch (InvalidOperationException) { throw new RpcException(new Status(StatusCode.FailedPrecondition, "The feed sequence has not been delivered.")); }
        return new AcknowledgeSurfaceFeedReply { AcknowledgedSequence = request.Sequence };
    }

    public override async Task<SubmitActionReply> SubmitAction(SubmitActionRequest request, ServerCallContext context)
    {
        var authenticated = Authenticate(context).Context;
        if (Encoding.UTF8.GetByteCount(request.InputJson) > MaximumInputBytes)
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "V2 action input is too large."));
        JsonElement input;
        try
        {
            if (string.IsNullOrWhiteSpace(request.InputJson))
            {
                input = JsonSerializer.SerializeToElement(new { });
            }
            else
            {
                using var document = JsonDocument.Parse(request.InputJson);
                input = document.RootElement.Clone();
            }
        }
        catch (JsonException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "V2 action input must be valid JSON.")); }
        if (input.ValueKind != JsonValueKind.Object)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "V2 action input must be a JSON object."));
        try { DemandSafeActionInput(input, depth: 0); }
        catch (ArgumentException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "V2 action input contains a forbidden authority or credential field.")); }

        V2ActionSubmission submission;
        V2ActionExecutor.Authorization authorization;
        try
        {
            authorization = await actionExecutor.ReserveAsync(authenticated, request.BindingId, request.ActionToken,
                request.SurfaceId, request.SurfaceRevision, input, context.CancellationToken).ConfigureAwait(false);
            submission = authorization.Submission;
        }
        catch (V2ActionRejectedException exception)
        {
            var status = exception.Reason == V2ActionRejection.Replay ? StatusCode.AlreadyExists : StatusCode.PermissionDenied;
            throw new RpcException(new Status(status, "V2 action authorization failed."));
        }

        // The session itself never receives brain.act. Only a successfully reauthorized, server-bound UI action
        // crosses into command admission, and its type comes from the stored binding rather than the request.
        var internalGrants = authenticated.Grants.Append("brain.act").ToHashSet(StringComparer.Ordinal);
        var commandContext = authenticated with
        {
            IdempotencyKey = submission.IdempotencyKey,
            Grants = internalGrants,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        var command = new V2CommandEnvelope(
            submission.ActionType,
            2,
            $"v2-ui-command-{Guid.NewGuid():N}",
            commandContext,
            submission.Input);
        V2OperationStatus operation;
        try
        {
            operation = await application.SubmitAsync(commandContext, command, context.CancellationToken).ConfigureAwait(false);
        }
        catch (V2IdempotencyConflictException)
        {
            actionExecutor.Release(authorization);
            throw new RpcException(new Status(StatusCode.AlreadyExists,
                "The action was already submitted with different input."));
        }
        catch
        {
            actionExecutor.Release(authorization);
            throw;
        }
        if (!actionExecutor.Commit(authorization, operation.OperationId))
            throw new RpcException(new Status(StatusCode.AlreadyExists, "V2 action authorization failed."));
        using var activity = ActivitySource.StartActivity("v2.ui.action.submit", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.action_type", submission.ActionType);
        activity?.SetTag("db.v2.ui.outcome", "accepted");
        logger.LogInformation("V2 UI action {ActionType} was accepted.", submission.ActionType);
        return new SubmitActionReply { OperationId = operation.OperationId, IdempotencyKey = submission.IdempotencyKey };
    }

    private AuthenticatedSession Authenticate(ServerCallContext context)
    {
        var metadata = ToMetadata(context.RequestHeaders);
        if (!V2GrpcAuthentication.TryAuthenticate(metadata, tokens, V2SessionAudiences.Ui, out var authenticated, out var expiresAt) ||
            !metadata.TryGetValue("x-v2-session", out var token))
            throw Unauthenticated();
        return new(authenticated, token, expiresAt);
    }

    private void Revalidate(AuthenticatedSession session)
    {
        if (!tokens.TryValidate(session.Token, V2SessionAudiences.Ui, out var current, out var expiresAt) ||
            expiresAt != session.ExpiresAt || current.TenantId != session.Context.TenantId ||
            current.WorkspaceId != session.Context.WorkspaceId || current.Principal != session.Context.Principal ||
            !string.Equals(current.SessionId, session.Context.SessionId, StringComparison.Ordinal))
            throw Unauthenticated();
    }

    private string Materialize(V2RequestContext recipient, V2StoredSurfaceRecord record, IReadOnlySet<string> capabilities)
    {
        try { return envelopeWriter.Write(recipient, record, capabilities); }
        catch (V2SurfaceCapabilityException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The V2 client does not support the required surface capabilities."));
        }
        catch (InvalidOperationException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The V2 surface protocol is incompatible with this client session."));
        }
    }

    private static void DemandAudience(ServerCallContext context)
    {
        var values = context.RequestHeaders.Where(entry => string.Equals(entry.Key, "x-v2-audience", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (values.Length != 1 || !string.Equals(values[0].Value, V2SessionAudiences.Ui, StringComparison.Ordinal))
            throw Unauthenticated();
    }

    private static Dictionary<string, string> ToMetadata(Metadata headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in headers)
        {
            if (entry.IsBinary || !result.TryAdd(entry.Key, entry.Value)) return new Dictionary<string, string>();
        }
        return result;
    }

    private static IReadOnlySet<string> ValidateCapabilities(IEnumerable<string> values)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !result.Add(value))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid V2 client capability declaration."));
            if (result.Count > 128) throw new RpcException(new Status(StatusCode.InvalidArgument, "Too many V2 client capabilities."));
        }
        return result;
    }

    private static void DemandSafeActionInput(JsonElement value, int depth)
    {
        if (depth > 64) throw new ArgumentException("V2 action input is too deeply nested.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                var normalized = new string(property.Name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                if (ForbiddenActionInputKeys.Contains(normalized))
                    throw new ArgumentException("Forbidden V2 action input field.");
                DemandSafeActionInput(property.Value, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) DemandSafeActionInput(item, depth + 1);
        }
    }

    private static readonly HashSet<string> ForbiddenActionInputKeys = new(StringComparer.Ordinal)
    {
        "accesstoken", "actiontoken", "authorization", "authorizationcode", "clientid", "clientsecret",
        "codeverifier", "grants", "password", "principal", "principalid", "refreshtoken", "secret",
        "secretvalue", "sessionid", "tenantid", "userid", "workspaceid"
    };

    private static V2SurfaceAudienceKind AudienceKind(FeedAudienceKind value) => (int)value switch
    {
        0 => V2SurfaceAudienceKind.Principal,
        1 => V2SurfaceAudienceKind.Workspace,
        2 => V2SurfaceAudienceKind.Public,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Unknown V2 feed audience."))
    };

    private static SessionReply ToReply(V2RequestContext authenticated, V2SessionPair pair) => new()
    {
        AccessToken = pair.AccessToken,
        RefreshToken = pair.RefreshToken,
        AccessExpiresAtUnixMs = pair.AccessExpiresAt.ToUnixTimeMilliseconds(),
        RefreshExpiresAtUnixMs = pair.RefreshExpiresAt.ToUnixTimeMilliseconds(),
        SessionId = authenticated.SessionId,
        TenantId = authenticated.TenantId.Value,
        WorkspaceId = authenticated.WorkspaceId.Value,
        PrincipalId = V2PrincipalScope.Id(authenticated.Principal)
    };

    private static void RecordDelivery(string eventKind, V2SurfaceAudienceKind audienceKind, int itemCount)
    {
        using var activity = ActivitySource.StartActivity("v2.ui.feed.deliver", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.event_kind", eventKind);
        activity?.SetTag("db.v2.ui.audience_kind", audienceKind.ToString().ToLowerInvariant());
        activity?.SetTag("db.v2.ui.item_count", itemCount);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private static RpcException Unauthenticated() =>
        new(new Status(StatusCode.Unauthenticated, "A valid V2 UI session for the exact transport audience is required."));

    private sealed record AuthenticatedSession(V2RequestContext Context, string Token, DateTimeOffset ExpiresAt);
}
