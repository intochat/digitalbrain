using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.V2.Ui.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record UiBootstrapOptions(
    string Secret,
    BrainOwnerId OwnerId,
    ActorId ActorId,
    TimeSpan AccessLifetime,
    IReadOnlySet<string> Grants,
    bool Enabled = true)
{
    public static UiBootstrapOptions FromConfiguration(IConfiguration configuration, RuntimeProfile profile)
    {
        var secret = configuration["DigitalBrain:Runtime:Ui:BootstrapSecret"] ?? string.Empty;
        if (profile == RuntimeProfile.Production)
        {
            if (!string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("DigitalBrain:Runtime:Ui:BootstrapSecret is forbidden in Production.");
            return new(
                string.Empty,
                new BrainOwnerId("disabled"),
                new ActorId("disabled"),
                TimeSpan.FromMinutes(15),
                new HashSet<string>(StringComparer.Ordinal),
                Enabled: false);
        }
        var owner = configuration["DigitalBrain:Runtime:Ui:OwnerId"] ?? "local-owner";
        var actor = configuration["DigitalBrain:Runtime:Ui:ActorId"] ?? "flutter-ui";
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(actor) ||
            owner.Length > 256 || actor.Length > 256)
            throw new InvalidOperationException("UI bootstrap identity configuration must be complete.");
        return new(secret, new(owner), new(actor), TimeSpan.FromMinutes(15),
            new HashSet<string>(StringComparer.Ordinal)
            {
                "brain.read", "ui.action", "gmail.read", "gmail.send", "salesforce.read", "salesforce.write"
            });
    }
}

public sealed record UiDeliveryOptions(
    TimeSpan ActionTokenRenewalInterval,
    TimeSpan AuthenticationRevalidationInterval)
{
    public static UiDeliveryOptions Default { get; } = new(TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(5));

    public UiDeliveryOptions Validate()
    {
        if (ActionTokenRenewalInterval <= TimeSpan.Zero || ActionTokenRenewalInterval >= UiProtocol.ActionTokenLifetime)
            throw new InvalidOperationException("UI action-token renewal must be positive and shorter than the action-token lifetime.");
        if (AuthenticationRevalidationInterval <= TimeSpan.Zero || AuthenticationRevalidationInterval > TimeSpan.FromMinutes(1))
            throw new InvalidOperationException("UI authentication revalidation must be between zero and one minute.");
        return this;
    }
}

public sealed class UiBootstrapAuthenticator(UiBootstrapOptions options)
{
    public bool TryAuthenticate(string suppliedSecret, out RuntimeRequestContext context)
    {
        context = default!;
        if (!options.Enabled || string.IsNullOrEmpty(options.Secret) || string.IsNullOrEmpty(suppliedSecret) ||
            !FixedTimeEquals(options.Secret, suppliedSecret)) return false;
        context = new RuntimeRequestContext(
            options.OwnerId,
            options.ActorId,
            new SessionId($"runtime-ui-session-{Guid.NewGuid():N}"),
            AuthAssurance.Password,
            Guid.NewGuid().ToString("N"),
            null,
            options.Grants);
        return true;
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}

public sealed class UiGrpcService(
    UiBootstrapAuthenticator bootstrap,
    UiExternalIdentityAuthenticator externalIdentity,
    RuntimeSessionAuthority sessions,
    RuntimeSurfaceFeed feed,
    SurfaceEnvelopeWriter envelopeWriter,
    McpInoCommandHandler conversationHandler,
    ConversationStateClient conversations,
    UiDeliveryOptions deliveryOptions,
    ILogger<UiGrpcService> logger) : DigitalBrainV2Ui.DigitalBrainV2UiBase
{
    private const int MaximumInputBytes = 64 * 1024;
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Mcp");

    public override async Task<SessionReply> BootstrapSession(BootstrapSessionRequest request, ServerCallContext context)
    {
        DemandAudience(context);
        var external = await externalIdentity.AuthenticateAsync(context).ConfigureAwait(false);
        RuntimeRequestContext bootstrapContext;
        var authenticationKind = "bootstrap";
        if (external.Status == UiExternalAuthenticationStatus.Authenticated)
        {
            bootstrapContext = external.Context!;
            authenticationKind = "oidc";
        }
        else if (external.Status == UiExternalAuthenticationStatus.Rejected ||
                 !bootstrap.TryAuthenticate(request.Secret, out bootstrapContext))
        {
            logger.LogWarning("UI bootstrap was denied.");
            throw Unauthenticated();
        }
        var issued = await sessions.CreateAsync(
            bootstrapContext,
            TimeSpan.FromMinutes(15),
            SessionAudiences.Ui,
            context.CancellationToken).ConfigureAwait(false);
        using var activity = ActivitySource.StartActivity("v2.ui.session.bootstrap", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.outcome", "success");
        activity?.SetTag("db.v2.ui.authentication_kind", authenticationKind);
        logger.LogInformation("UI bootstrap issued an owner-scoped session.");
        return ToReply(issued.Context, issued.Pair);
    }

    public override async Task<SessionReply> RefreshSession(RefreshSessionRequest request, ServerCallContext context)
    {
        DemandAudience(context);
        var issued = string.IsNullOrWhiteSpace(request.RefreshToken)
            ? null
            : await sessions.RefreshAsync(
                request.RefreshToken,
                TimeSpan.FromMinutes(15),
                SessionAudiences.Ui,
                context.CancellationToken).ConfigureAwait(false);
        if (issued is null)
        {
            logger.LogWarning("UI session refresh was denied.");
            throw Unauthenticated();
        }
        using var activity = ActivitySource.StartActivity("v2.ui.session.refresh", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.outcome", "success");
        return ToReply(issued.Context, issued.Pair);
    }

    public override async Task<LogoutSessionReply> LogoutSession(LogoutSessionRequest request, ServerCallContext context)
    {
        DemandAudience(context);
        if (string.IsNullOrWhiteSpace(request.RefreshToken) ||
            !await sessions.RevokeAsync(
                request.RefreshToken,
                SessionAudiences.Ui,
                context.CancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("UI session logout was denied.");
            throw Unauthenticated();
        }
        using var activity = ActivitySource.StartActivity("v2.ui.session.logout", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.outcome", "success");
        logger.LogInformation("UI session was revoked.");
        return new LogoutSessionReply();
    }

    public override async Task WatchSurfaceFeed(
        WatchSurfaceFeedRequest request,
        IServerStreamWriter<SurfaceFeedEvent> responseStream,
        ServerCallContext context)
    {
        var session = await AuthenticateAsync(context).ConfigureAwait(false);
        var authenticated = session.Context;
        if (request.AfterSequence < 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "after_sequence cannot be negative."));
        var audienceKind = AudienceKind(request.Audience);
        if (audienceKind != SurfaceAudienceKind.Actor)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Only the authenticated actor feed is supported."));
        var capabilities = ValidateCapabilities(request.ClientCapabilities);
        var batchSize = Math.Clamp(request.MaxBatchSize <= 0 ? 50 : request.MaxBatchSize, 1, 100);
        logger.LogInformation("UI feed opened for {AudienceKind} audience.", audienceKind);
        var cursor = request.AfterSequence;
        var delivered = false;
        var prepared = await feed.PrepareSessionAsync(authenticated, context.CancellationToken).ConfigureAwait(false);
        var state = prepared.State;
        var actionTokens = prepared.ActionTokens;
        IReadOnlyList<SurfaceActionBinding> tokenBindings = state.ActionBindings;
        var nextActionRenewal = DateTimeOffset.UtcNow.Add(deliveryOptions.ActionTokenRenewalInterval);
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var leaseRemaining = session.ExpiresAt - DateTimeOffset.UtcNow;
        if (leaseRemaining <= TimeSpan.Zero) throw Unauthenticated();
        leaseCancellation.CancelAfter(leaseRemaining);
        try
        {
            while (!leaseCancellation.IsCancellationRequested)
            {
                await RevalidateAsync(session, leaseCancellation.Token).ConfigureAwait(false);
                state = await feed.ReadAsync(authenticated, leaseCancellation.Token).ConfigureAwait(false);
                if (DateTimeOffset.UtcNow >= nextActionRenewal ||
                    ActionBindingsChanged(tokenBindings, state.ActionBindings))
                {
                    prepared = await feed.PrepareSessionAsync(authenticated, leaseCancellation.Token).ConfigureAwait(false);
                    state = prepared.State;
                    actionTokens = prepared.ActionTokens;
                    tokenBindings = state.ActionBindings;
                    nextActionRenewal = DateTimeOffset.UtcNow.Add(deliveryOptions.ActionTokenRenewalInterval);
                }
                var page = feed.ReadPage(
                    authenticated,
                    state,
                    cursor,
                    batchSize);
                if (page.ResetRequired)
                {
                    var reset = new SurfaceFeedReset
                    {
                        Reason = "sequence-retention-gap",
                        ResumeSequence = page.LatestSequence
                    };
                    foreach (var item in page.Items)
                        reset.SnapshotJson.Add(Materialize(authenticated, item, capabilities, actionTokens));
                    await RevalidateAsync(session, leaseCancellation.Token).ConfigureAwait(false);
                    await responseStream.WriteAsync(new SurfaceFeedEvent { Reset = reset }).ConfigureAwait(false);
                    if (page.LatestSequence > 0)
                        await feed.RecordDeliveredAsync(authenticated, page.LatestSequence, leaseCancellation.Token).ConfigureAwait(false);
                    cursor = page.LatestSequence;
                    RecordDelivery("reset", audienceKind, reset.SnapshotJson.Count);
                    delivered = true;
                    continue;
                }

                foreach (var item in page.Items)
                {
                    leaseCancellation.Token.ThrowIfCancellationRequested();
                    await RevalidateAsync(session, leaseCancellation.Token).ConfigureAwait(false);
                    var feedEvent = new SurfaceFeedEvent
                    {
                        SurfaceJson = Materialize(authenticated, item, capabilities, actionTokens)
                    };
                    await responseStream.WriteAsync(feedEvent).ConfigureAwait(false);
                    await feed.RecordDeliveredAsync(authenticated, item.Sequence, leaseCancellation.Token).ConfigureAwait(false);
                    cursor = item.Sequence;
                    if (!delivered)
                    {
                        RecordDelivery("surface", audienceKind, 1);
                        delivered = true;
                    }
                }
                var now = DateTimeOffset.UtcNow;
                var revalidateAfter = deliveryOptions.AuthenticationRevalidationInterval;
                var renewAfter = nextActionRenewal - now;
                var wakeAfter = renewAfter < revalidateAfter ? renewAfter : revalidateAfter;
                if (wakeAfter <= TimeSpan.Zero)
                    continue;
                using var wakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(leaseCancellation.Token);
                wakeCancellation.CancelAfter(wakeAfter);
                try
                {
                    await feed.WaitForChangeAsync(authenticated, cursor, wakeCancellation.Token).ConfigureAwait(false);
                    await RevalidateAsync(session, leaseCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!leaseCancellation.IsCancellationRequested)
                {
                    await RevalidateAsync(session, leaseCancellation.Token).ConfigureAwait(false);
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
            logger.LogInformation("UI feed closed for {AudienceKind} audience after delivery={Delivered}.", audienceKind, delivered);
        }
    }

    public override async Task<AcknowledgeSurfaceFeedReply> AcknowledgeSurfaceFeed(AcknowledgeSurfaceFeedRequest request, ServerCallContext context)
    {
        var authenticated = (await AuthenticateAsync(context).ConfigureAwait(false)).Context;
        if (request.Sequence < 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "sequence cannot be negative."));
        var audienceKind = AudienceKind(request.Audience);
        if (audienceKind != SurfaceAudienceKind.Actor)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Only the authenticated actor feed is supported."));
        try
        {
            var acknowledged = await feed.AcknowledgeAsync(
                authenticated,
                request.Sequence,
                context.CancellationToken).ConfigureAwait(false);
            return new AcknowledgeSurfaceFeedReply { AcknowledgedSequence = acknowledged };
        }
        catch (InvalidOperationException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The feed sequence has not been delivered."));
        }
    }

    public override async Task<SubmitActionReply> SubmitAction(SubmitActionRequest request, ServerCallContext context)
    {
        var authenticated = (await AuthenticateAsync(context).ConfigureAwait(false)).Context;
        if (Encoding.UTF8.GetByteCount(request.InputJson) > MaximumInputBytes)
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Action input is too large."));
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
        catch (JsonException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Action input must be valid JSON.")); }
        if (input.ValueKind != JsonValueKind.Object)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Action input must be a JSON object."));
        try { SurfacePayloadPolicy.DemandSafe(input); }
        catch (ArgumentException) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Action input contains a forbidden authority or credential field.")); }

        AuthorizedRuntimeAction authorized;
        try
        {
            authorized = await feed.AuthorizeActionAsync(
                authenticated,
                request.BindingId,
                request.ActionToken,
                request.SurfaceId,
                request.SurfaceRevision,
                input,
                context.CancellationToken).ConfigureAwait(false);
        }
        catch (ActionRejectedException exception)
        {
            logger.LogWarning(
                "UI action authorization was rejected with {RejectionReason}; UI action grant present={HasUiActionGrant}.",
                exception.Reason,
                authenticated.Grants.Contains("ui.action"));
            var status = StatusForActionRejection(exception.Reason);
            throw new RpcException(new Status(status, "Action authorization failed."));
        }

        var submission = authorized.Submission;
        if (string.Equals(submission.ActionType, ConversationSurfacePayload.ApprovalActionType, StringComparison.Ordinal))
        {
            if (!RuntimeSurfaceFeed.TryReadApprovalDecision(submission.Input, out var decision) ||
                !string.Equals(decision.OperationId, submission.OperationId, StringComparison.Ordinal))
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Action authorization failed."));
            var approvalReceipt = await conversations.DecideApprovalAsync(
                authenticated with { ConversationId = authorized.ConversationId },
                decision.OperationId,
                decision.ApprovalId,
                decision.Approved,
                submission.IdempotencyKey,
                CancellationToken.None).ConfigureAwait(false);
            return Accepted(submission with
            {
                OperationId = approvalReceipt.OperationId,
                IdempotencyKey = approvalReceipt.IdempotencyKey
            });
        }
        if (!string.Equals(submission.ActionType, ConversationSurfacePayload.SendActionType, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Action authorization failed."));
        var internalGrants = authenticated.Grants.Append("brain.interact").ToHashSet(StringComparer.Ordinal);
        var commandContext = authenticated with
        {
            IdempotencyKey = submission.IdempotencyKey,
            Grants = internalGrants,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ConversationId = authorized.ConversationId
        };
        var command = new CommandEnvelope(
            submission.ActionType,
            2,
            submission.IdempotencyKey,
            commandContext,
            submission.Input);
        var receipt = await conversationHandler.AcceptAsync(command).ConfigureAwait(false);
        return Accepted(submission with
        {
            OperationId = receipt.OperationId,
            IdempotencyKey = receipt.IdempotencyKey
        });
    }

    private SubmitActionReply Accepted(ActionSubmission submission)
    {
        using var activity = ActivitySource.StartActivity("v2.ui.action.submit", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.action_type", submission.ActionType);
        activity?.SetTag("db.v2.ui.outcome", "accepted");
        logger.LogInformation("UI action {ActionType} was accepted.", submission.ActionType);
        return new() { OperationId = submission.OperationId, IdempotencyKey = submission.IdempotencyKey };
    }

    internal static StatusCode StatusForActionRejection(ActionRejection reason) => reason switch
    {
        ActionRejection.Replay => StatusCode.AlreadyExists,
        ActionRejection.WrongRevision or ActionRejection.Unavailable => StatusCode.FailedPrecondition,
        _ => StatusCode.PermissionDenied
    };

    internal static bool ActionBindingsChanged(
        IReadOnlyList<SurfaceActionBinding> issuedBindings,
        IReadOnlyList<SurfaceActionBinding> currentBindings) =>
        !issuedBindings.SequenceEqual(currentBindings);

    private async Task<AuthenticatedSession> AuthenticateAsync(ServerCallContext context)
    {
        var metadata = ToMetadata(context.RequestHeaders);
        if (!metadata.TryGetValue("x-v2-session", out var token))
            throw Unauthenticated();
        var validated = await sessions.ValidateAccessAsync(
            token,
            SessionAudiences.Ui,
            context.CancellationToken).ConfigureAwait(false);
        return validated is null
            ? throw Unauthenticated()
            : new(validated.Context, token, validated.AccessExpiresAt, validated.SessionVersion);
    }

    private async Task RevalidateAsync(AuthenticatedSession session, CancellationToken cancellationToken)
    {
        var validated = await sessions.ValidateAccessAsync(
            session.Token,
            SessionAudiences.Ui,
            cancellationToken).ConfigureAwait(false);
        if (validated is null || validated.AccessExpiresAt != session.ExpiresAt ||
            validated.SessionVersion != session.SessionVersion ||
            validated.Context.OwnerId != session.Context.OwnerId ||
            validated.Context.ActorId != session.Context.ActorId ||
            validated.Context.SessionId != session.Context.SessionId)
            throw Unauthenticated();
    }

    private string Materialize(
        RuntimeRequestContext recipient,
        StoredSurfaceRecord record,
        IReadOnlySet<string> capabilities,
        IReadOnlyDictionary<string, SurfaceActionToken> actionTokens)
    {
        try { return envelopeWriter.Write(recipient, record, capabilities, actionTokens); }
        catch (SurfaceCapabilityException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The client does not support the required surface capabilities."));
        }
        catch (InvalidOperationException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "The surface protocol is incompatible with this client session."));
        }
    }

    private static void DemandAudience(ServerCallContext context)
    {
        var values = context.RequestHeaders.Where(entry => string.Equals(entry.Key, "x-v2-audience", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (values.Length != 1 || !string.Equals(values[0].Value, SessionAudiences.Ui, StringComparison.Ordinal))
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
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid client capability declaration."));
            if (result.Count > 128) throw new RpcException(new Status(StatusCode.InvalidArgument, "Too many client capabilities."));
        }
        return result;
    }

    private static SurfaceAudienceKind AudienceKind(FeedAudienceKind value) => (int)value switch
    {
        0 => SurfaceAudienceKind.Actor,
        1 => SurfaceAudienceKind.Owner,
        2 => SurfaceAudienceKind.Public,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Unknown feed audience."))
    };

    private static SessionReply ToReply(RuntimeRequestContext authenticated, SessionPair pair) => new()
    {
        AccessToken = pair.AccessToken,
        RefreshToken = pair.RefreshToken,
        AccessExpiresAtUnixMs = pair.AccessExpiresAt.ToUnixTimeMilliseconds(),
        RefreshExpiresAtUnixMs = pair.RefreshExpiresAt.ToUnixTimeMilliseconds(),
        SessionId = authenticated.SessionId.Value,
        OwnerId = authenticated.OwnerId.Value,
        ActorId = ActorScope.Id(authenticated.ActorId)
    };

    private static void RecordDelivery(string eventKind, SurfaceAudienceKind audienceKind, int itemCount)
    {
        using var activity = ActivitySource.StartActivity("v2.ui.feed.deliver", ActivityKind.Internal);
        activity?.SetTag("db.v2.ui.event_kind", eventKind);
        activity?.SetTag("db.v2.ui.audience_kind", audienceKind.ToString().ToLowerInvariant());
        activity?.SetTag("db.v2.ui.item_count", itemCount);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private static RpcException Unauthenticated() =>
        new(new Status(StatusCode.Unauthenticated, "A valid UI session for the exact transport audience is required."));

    private sealed record AuthenticatedSession(
        RuntimeRequestContext Context,
        string Token,
        DateTimeOffset ExpiresAt,
        long SessionVersion);
}
