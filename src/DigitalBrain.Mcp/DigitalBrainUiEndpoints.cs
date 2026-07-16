using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Grpc.Core;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
using GrpcFeatureBehavior = DigitalBrain.V2.Ui.Grpc.FeatureBehavior;
using GrpcFeatureDraft = DigitalBrain.V2.Ui.Grpc.FeatureDraft;
using GrpcFeatureDraftPatch = DigitalBrain.V2.Ui.Grpc.FeatureDraftPatch;
using GrpcFeatureDraftStatus = DigitalBrain.V2.Ui.Grpc.FeatureDraftStatus;
using GrpcFeatureGrant = DigitalBrain.V2.Ui.Grpc.FeatureGrant;
using GrpcFeatureRelease = DigitalBrain.V2.Ui.Grpc.FeatureRelease;
using GrpcFeatureScenario = DigitalBrain.V2.Ui.Grpc.FeatureScenario;
using GrpcFeatureSourceFile = DigitalBrain.V2.Ui.Grpc.FeatureSourceFile;
using GrpcFeatureSourceKind = DigitalBrain.V2.Ui.Grpc.FeatureSourceKind;
using GrpcFeatureSourceSnapshot = DigitalBrain.V2.Ui.Grpc.FeatureSourceSnapshot;
using GrpcOriginatingRequest = DigitalBrain.V2.Ui.Grpc.OriginatingRequest;
using GrpcFeatureRunAuthorityState = DigitalBrain.V2.Ui.Grpc.FeatureRunAuthorityState;
using GrpcFeatureRunOrigin = DigitalBrain.V2.Ui.Grpc.FeatureRunOrigin;
using GrpcFeatureRunSnapshot = DigitalBrain.V2.Ui.Grpc.FeatureRunSnapshot;
using GrpcFeatureRunStatus = DigitalBrain.V2.Ui.Grpc.FeatureRunStatus;
using DigitalBrain.V2.Ui.Grpc;

namespace DigitalBrain.Mcp;

public sealed class DigitalBrainUiEndpoints(
    FeatureAuthoringService authoring,
    FeatureSuggestionService suggestions,
    ILogger<DigitalBrainUiEndpoints> logger,
    DigitalBrainQueryService? queries = null)
{
    private const int MaximumVerificationEvidenceUtf8Bytes = 2 * 1024 * 1024;
    private const int MaximumRunAttempts = 5;
    private static readonly char[] InvalidSourcePathCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly HashSet<string> ReservedSourcePathSegments = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<FeatureDraftReply> GetFeatureDraftAsync(
        RuntimeRequestContext context,
        GetFeatureDraftRequest request,
        CancellationToken cancellationToken)
    {
        var draftId = MapRequest(context, () => new FeatureDraftId(Identifier(request.DraftId, 128)));
        var read = await InvokeAsync(() => authoring.ReadWithRecoveryAsync(context, draftId, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectDraft(draftId, read));
    }

    public async Task<FeatureDraftReply> ResetFeatureDraftInstallationAsync(
        RuntimeRequestContext context,
        ResetFeatureDraftInstallationRequest request,
        CancellationToken cancellationToken)
    {
        var input = MapRequest(context, () => (
            DraftId: new FeatureDraftId(Identifier(request.DraftId, 128)),
            IdempotencyId: Identifier(request.IdempotencyId, 256)));
        var read = await InvokeAsync(() => authoring.ResetInstallationReservationAsync(
                context,
                input.DraftId,
                input.IdempotencyId,
                cancellationToken))
            .ConfigureAwait(false);
        return Project(() => ProjectDraft(input.DraftId, read));
    }

    public async Task<FeatureDraftReply> ReviseFeatureDraftAsync(
        RuntimeRequestContext context,
        ReviseFeatureDraftRequest request,
        CancellationToken cancellationToken)
    {
        var input = MapRequest(context, () => MapRevision(request));
        var draft = await InvokeAsync(() => input.Command switch
        {
            ReviseBehaviorCommand command => authoring.ReviseBehaviorAsync(
                context,
                input.DraftId,
                command.Behavior,
                input.ExpectedRevision,
                input.IdempotencyId,
                cancellationToken),
            ReviseSourceCommand command => authoring.ReviseSourceAsync(
                context,
                input.DraftId,
                command.Source,
                input.ExpectedRevision,
                input.IdempotencyId,
                cancellationToken),
            AcceptPatchCommand command => authoring.AcceptSuggestedChangeAsync(
                context,
                command.Patch,
                input.ExpectedRevision,
                input.IdempotencyId,
                cancellationToken),
            RejectPatchCommand command => authoring.RejectSuggestedChangeAsync(
                context,
                new RejectSuggestedChange(
                    input.DraftId,
                    command.PatchId,
                    command.BaseRevision,
                    input.ExpectedRevision),
                cancellationToken),
            _ => throw new InvalidOperationException("Unknown mapped revision command.")
        }).ConfigureAwait(false);
        return Project(() => ProjectRevision(input, draft));
    }

    public async Task<FeatureDraftPatchReply> SuggestFeatureChangeAsync(
        RuntimeRequestContext context,
        SuggestFeatureChangeRequest request,
        CancellationToken cancellationToken)
    {
        var command = MapRequest(context, () => new SuggestFeatureChange(
            new FeatureDraftId(Identifier(request.DraftId, 128)),
            Revision(request.HasExpectedRevision, request.ExpectedRevision),
            Text(request.Guidance, 4096),
            Identifier(request.SuggestionId, 256)));
        var patch = await InvokeAsync(() => suggestions.SuggestAsync(context, command, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectSuggestion(command, patch));
    }

    public async Task<FeatureReleaseReviewReply> VerifyFeatureDraftAsync(
        RuntimeRequestContext context,
        VerifyFeatureDraftRequest request,
        CancellationToken cancellationToken)
    {
        var command = MapRequest(context, () => new VerifyFeatureDraft(
            new FeatureDraftId(Identifier(request.DraftId, 128)),
            Revision(request.HasExpectedRevision, request.ExpectedRevision),
            Identifier(request.IdempotencyId, 256)));
        var review = await InvokeAsync(() => authoring.RunVerificationAsync(context, command, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectVerification(command, review));
    }

    public async Task<FeatureAccessReviewReply> ReviewFeatureAccessAsync(
        RuntimeRequestContext context,
        ReviewFeatureAccessRequest request,
        CancellationToken cancellationToken)
    {
        var command = MapRequest(context, () => MapAccessReview(request));
        var review = await InvokeAsync(() => authoring.PrepareAccessReviewAsync(context, command, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectAccessReview(command, review));
    }

    public async Task<FeatureInstallReply> InstallFeatureVersionAsync(
        RuntimeRequestContext context,
        InstallFeatureVersionRequest request,
        CancellationToken cancellationToken)
    {
        var command = MapRequest(context, () => MapInstall(request));
        var installed = await InvokeAsync(() => authoring.InstallAsync(context, command, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectInstallation(command, context.ActorId, installed));
    }

    public async Task<ResumeOriginatingRequestReply> ResumeOriginatingRequestAsync(
        RuntimeRequestContext context,
        ResumeOriginatingRequestRequest request,
        McpInoCommandHandler conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        var input = MapRequest(context, () => new ResumeOriginatingRequestInput(
            new FeatureDraftId(Identifier(request.DraftId, 128)),
            Revision(request.HasExpectedRevision, request.ExpectedRevision),
            Identifier(request.IdempotencyId, 256)));
        var snapshot = await InvokeAsync(() =>
            authoring.ReadWithRecoveryAsync(context, input.DraftId, cancellationToken)).ConfigureAwait(false);
        var draft = snapshot.Draft;
        if (draft.Revision != input.ExpectedRevision)
            throw Status(StatusCode.Aborted, "The Feature Draft changed. Reload it and retry.");
        if (!string.Equals(draft.Status, "installed", StringComparison.Ordinal) ||
            draft.InstallationId is not { } installationId ||
            snapshot.Recovery is not { Installed: true } recovery ||
            recovery.InstallationId != installationId ||
            recovery.Release.Digest != recovery.Verification.Release ||
            recovery.Paused)
            throw Status(StatusCode.FailedPrecondition, "The Feature Draft is not ready for this operation.");
        var origin = draft.OriginatingRequest;
        if (!string.Equals(origin.ConversationId, InoConversationIdentity.From(context), StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(origin.OperationId) ||
            string.IsNullOrWhiteSpace(origin.Text) ||
            origin.Text.Length > 4096 ||
            !string.Equals(origin.Text, origin.Text.Trim(), StringComparison.Ordinal))
            throw Status(StatusCode.FailedPrecondition, "The Feature Draft is not ready for this operation.");
        var grants = new HashSet<string>(context.Grants, StringComparer.Ordinal) { "brain.interact" };
        var commandContext = context with
        {
            CorrelationId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = input.IdempotencyId,
            Grants = grants,
            ConversationId = origin.ConversationId
        };
        var receipt = await InvokeAsync(() => conversation.AcceptAsync(new CommandEnvelope(
            McpInoCommandHandler.CommandType,
            2,
            input.IdempotencyId,
            commandContext,
            JsonSerializer.SerializeToElement(new { prompt = origin.Text })))).ConfigureAwait(false);
        return new ResumeOriginatingRequestReply
        {
            CommandId = receipt.IdempotencyKey,
            OperationId = receipt.OperationId,
            Phase = InoOperationPhase.Accepted.ToString(),
            Version = 1
        };
    }

    public async Task<FeatureReply> GetFeatureAsync(
        RuntimeRequestContext context,
        GetFeatureRequest request,
        CancellationToken cancellationToken)
    {
        var draftId = MapRequest(context, () => new FeatureDraftId(Identifier(request.FeatureId, 128)));
        var detail = await InvokeAsync(() => authoring.ReadInstalledAsync(context, draftId, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectFeature(draftId, context.ActorId, detail));
    }

    public async Task<FeatureReleaseSourceReply> GetFeatureReleaseSourceAsync(
        RuntimeRequestContext context,
        GetFeatureReleaseSourceRequest request,
        CancellationToken cancellationToken)
    {
        var coordinate = MapRequest(context, () => new FeatureReleaseSourceCoordinate(
            new FeatureDraftId(Identifier(request.FeatureId, 128)),
            new FeatureInstallationId(Identifier(request.InstallationId, 256)),
            new ReleaseDigest(ReleaseDigest(request.ReleaseDigest)),
            SourceReference(request.SourceReference)));
        var detail = await InvokeAsync(() => authoring.ReadInstalledAsync(
            context,
            coordinate.DraftId,
            cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectFeatureReleaseSource(
            coordinate.DraftId,
            coordinate.InstallationId,
            coordinate.Release,
            coordinate.SourceReference,
            context.ActorId,
            detail));
    }

    public async Task<FeatureReply> RollbackFeatureVersionAsync(
        RuntimeRequestContext context,
        RollbackFeatureVersionRequest request,
        CancellationToken cancellationToken)
    {
        var command = MapRequest(context, () => new RollbackFeatureVersion(
            new FeatureDraftId(Identifier(request.FeatureId, 128)),
            new ReleaseDigest(ReleaseDigest(request.ExpectedActiveDigest)),
            new ReleaseDigest(ReleaseDigest(request.TargetDigest)),
            Identifier(request.IdempotencyId, 256),
            Revision(request.HasExpectedRevision, request.ExpectedRevision)));
        var detail = await InvokeAsync(() => authoring.RollbackAsync(context, command, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectFeature(command.DraftId, context.ActorId, detail));
    }

    public async Task<ListActivityReply> ListActivityAsync(
        RuntimeRequestContext context,
        ListActivityRequest request,
        CancellationToken cancellationToken)
    {
        var input = MapActivityRequest(context, request);
        var runs = await InvokeQueryAsync(() => QueryService.ListRunsAsync(
                context,
                input.Status,
                input.Origin,
                input.FeatureId,
                input.Limit,
                cancellationToken))
            .ConfigureAwait(false);
        return ProjectQuery(() =>
        {
            var reply = new ListActivityReply();
            reply.Runs.Add(runs.Select(ToReply));
            return reply;
        });
    }

    public async Task<RunReply> GetRunAsync(
        RuntimeRequestContext context,
        GetRunRequest request,
        CancellationToken cancellationToken)
    {
        DemandActivityAuthority(context);
        string runId;
        try
        {
            runId = Identifier(request.RunId, 256);
        }
        catch (ArgumentException)
        {
            throw Status(StatusCode.InvalidArgument, "The Activity request is invalid.");
        }
        var run = await InvokeQueryAsync(() => QueryService.GetRunAsync(context, runId, cancellationToken))
            .ConfigureAwait(false);
        return ProjectQuery(() => new RunReply { Run = ToReply(run) });
    }

    public async Task<GetConversationContextReply> GetConversationContextAsync(
        RuntimeRequestContext context,
        GetConversationContextRequest request,
        ConversationStateClient conversations,
        CancellationToken cancellationToken)
    {
        DemandActivityAuthority(context);
        string conversationId;
        string requestId;
        try
        {
            conversationId = request.ConversationId;
            ConversationStateClient.DemandConversationId(conversationId);
            requestId = Identifier(request.RequestId, 256);
        }
        catch (ArgumentException)
        {
            throw Status(StatusCode.InvalidArgument, "The Chat context request is invalid.");
        }
        InoConversationRequest? originatingRequest;
        try
        {
            originatingRequest = await conversations.ReadRequestAsync(
                    context with { ConversationId = conversationId },
                    requestId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            originatingRequest = null;
        }
        if (originatingRequest is null)
            throw Status(StatusCode.NotFound, "The Chat context was not found.");
        return new GetConversationContextReply
        {
            ConversationId = originatingRequest.ConversationId,
            RequestId = originatingRequest.RequestId,
            RequestText = originatingRequest.Text
        };
    }

    private DigitalBrainQueryService QueryService => queries ??
        throw Status(StatusCode.Unavailable, "Activity is temporarily unavailable. Retry the same request.");

    private static ActivityQueryInput MapActivityRequest(
        RuntimeRequestContext context,
        ListActivityRequest request)
    {
        DemandActivityAuthority(context);
        try
        {
            DigitalBrain.Kernel.Contracts.FeatureRunStatus? status = request.HasStatus
                ? request.Status switch
                {
                    GrpcFeatureRunStatus.Queued => DigitalBrain.Kernel.Contracts.FeatureRunStatus.Queued,
                    GrpcFeatureRunStatus.Running => DigitalBrain.Kernel.Contracts.FeatureRunStatus.Running,
                    GrpcFeatureRunStatus.WaitingForApproval => DigitalBrain.Kernel.Contracts.FeatureRunStatus.WaitingForApproval,
                    GrpcFeatureRunStatus.Completed => DigitalBrain.Kernel.Contracts.FeatureRunStatus.Completed,
                    GrpcFeatureRunStatus.Failed => DigitalBrain.Kernel.Contracts.FeatureRunStatus.Failed,
                    GrpcFeatureRunStatus.Parked => DigitalBrain.Kernel.Contracts.FeatureRunStatus.Parked,
                    _ => throw new ArgumentException("A concrete Run status is required.")
                }
                : null;
            DigitalBrain.Kernel.Contracts.FeatureRunOrigin? origin = request.HasOrigin
                ? request.Origin switch
                {
                    GrpcFeatureRunOrigin.Chat => DigitalBrain.Kernel.Contracts.FeatureRunOrigin.Chat,
                    GrpcFeatureRunOrigin.Direct => DigitalBrain.Kernel.Contracts.FeatureRunOrigin.Direct,
                    GrpcFeatureRunOrigin.Schedule => DigitalBrain.Kernel.Contracts.FeatureRunOrigin.Schedule,
                    GrpcFeatureRunOrigin.Event => DigitalBrain.Kernel.Contracts.FeatureRunOrigin.Event,
                    _ => throw new ArgumentException("A concrete Run origin is required.")
                }
                : null;
            var featureId = request.HasFeatureId
                ? new FeatureDraftId(Identifier(request.FeatureId, 128))
                : null;
            var limit = request.Limit == 0 ? DigitalBrainQueryService.DefaultListLimit : request.Limit;
            if (limit is < 1 or > DigitalBrainQueryService.MaximumListLimit)
                throw new ArgumentOutOfRangeException(nameof(request));
            return new ActivityQueryInput(status, origin, featureId, limit);
        }
        catch (ArgumentException)
        {
            throw Status(StatusCode.InvalidArgument, "The Activity request is invalid.");
        }
    }

    private static void DemandActivityAuthority(RuntimeRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Grants.Contains("brain.read"))
            throw Status(StatusCode.PermissionDenied, "Activity read authority is required.");
    }

    private async Task<T> InvokeQueryAsync<T>(Func<Task<T>> invocation)
    {
        try
        {
            return await invocation().ConfigureAwait(false);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            throw Status(StatusCode.NotFound, "The requested Run was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            throw Status(StatusCode.PermissionDenied, "Activity read authority is required.");
        }
        catch (ArgumentException)
        {
            throw Status(StatusCode.InvalidArgument, "The Activity request is invalid.");
        }
        catch (TimeoutException)
        {
            throw Status(StatusCode.Unavailable, "Activity is temporarily unavailable. Retry the same request.");
        }
        catch (IOException)
        {
            throw Status(StatusCode.Unavailable, "Activity is temporarily unavailable. Retry the same request.");
        }
        catch (OrleansException)
        {
            throw Status(StatusCode.Unavailable, "Activity is temporarily unavailable. Retry the same request.");
        }
        catch (Exception)
        {
            logger.LogError("An Activity query failed safely.");
            throw Status(StatusCode.Internal, "Activity could not be loaded.");
        }
    }

    private T ProjectQuery<T>(Func<T> projection)
    {
        try
        {
            return projection();
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("An Activity response projection failed safely.");
            throw Status(StatusCode.Internal, "Activity could not be loaded.");
        }
    }

    private static GrpcFeatureRunSnapshot ToReply(DigitalBrainRun value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var run = value.Run;
        var reply = new GrpcFeatureRunSnapshot
        {
            RunId = Identifier(run.RunId, 256),
            FeatureId = Identifier(value.FeatureId.Value, 128),
            FeatureName = Text(value.FeatureGoal, 4096),
            InstallationId = Identifier(run.InstallationId.Value, 256),
            ReleaseDigest = ReleaseDigest(run.Release.Value),
            InputKind = Identifier(run.InputKind, 128),
            Origin = run.Origin switch
            {
                DigitalBrain.Kernel.Contracts.FeatureRunOrigin.Chat => GrpcFeatureRunOrigin.Chat,
                DigitalBrain.Kernel.Contracts.FeatureRunOrigin.Direct => GrpcFeatureRunOrigin.Direct,
                DigitalBrain.Kernel.Contracts.FeatureRunOrigin.Schedule => GrpcFeatureRunOrigin.Schedule,
                DigitalBrain.Kernel.Contracts.FeatureRunOrigin.Event => GrpcFeatureRunOrigin.Event,
                _ => throw new InvalidDataException("Invalid Run origin.")
            },
            Status = run.Status switch
            {
                DigitalBrain.Kernel.Contracts.FeatureRunStatus.Queued => GrpcFeatureRunStatus.Queued,
                DigitalBrain.Kernel.Contracts.FeatureRunStatus.Running => GrpcFeatureRunStatus.Running,
                DigitalBrain.Kernel.Contracts.FeatureRunStatus.WaitingForApproval => GrpcFeatureRunStatus.WaitingForApproval,
                DigitalBrain.Kernel.Contracts.FeatureRunStatus.Completed => GrpcFeatureRunStatus.Completed,
                DigitalBrain.Kernel.Contracts.FeatureRunStatus.Failed => GrpcFeatureRunStatus.Failed,
                DigitalBrain.Kernel.Contracts.FeatureRunStatus.Parked => GrpcFeatureRunStatus.Parked,
                _ => throw new InvalidDataException("Invalid Run status.")
            },
            AuthorityState = run.AuthorityState switch
            {
                DigitalBrain.Kernel.Contracts.FeatureRunAuthorityState.Authorized => GrpcFeatureRunAuthorityState.Authorized,
                DigitalBrain.Kernel.Contracts.FeatureRunAuthorityState.WaitingForApproval => GrpcFeatureRunAuthorityState.WaitingForApproval,
                DigitalBrain.Kernel.Contracts.FeatureRunAuthorityState.Paused => GrpcFeatureRunAuthorityState.Paused,
                _ => throw new InvalidDataException("Invalid Run authority state.")
            },
            OccurredAtUnixMs = run.OccurredAt.ToUnixTimeMilliseconds(),
            Attempts = run.Attempts is >= 0 and <= MaximumRunAttempts
                ? run.Attempts
                : throw new InvalidDataException("Invalid Run attempt count."),
            TraceReference = Identifier(run.TraceReference, 256)
        };
        if (run.OriginReference is { } reference)
        {
            reply.OriginReference = new DigitalBrain.V2.Ui.Grpc.FeatureRunOriginReference();
            if (reference.ConversationId is { } conversationId)
                reply.OriginReference.ConversationId = Identifier(conversationId, 256);
            if (reference.RequestId is { } requestId)
                reply.OriginReference.RequestId = Identifier(requestId, 256);
            if (reference.AutomationId is { } automationId)
                reply.OriginReference.AutomationId = Identifier(automationId, 256);
        }
        if (run.StartedAt is { } startedAt)
            reply.StartedAtUnixMs = startedAt.ToUnixTimeMilliseconds();
        if (run.CompletedAt is { } completedAt)
            reply.CompletedAtUnixMs = completedAt.ToUnixTimeMilliseconds();
        if (run.RetryAt is { } retryAt)
            reply.RetryAtUnixMs = retryAt.ToUnixTimeMilliseconds();
        if (run.ResultSurfaceReference is { } resultSurfaceReference)
            reply.ResultSurfaceReference = Identifier(resultSurfaceReference, 256);
        if (run.SafeFailure is { } safeFailure)
            reply.SafeFailure = Text(safeFailure, 256);
        if (run.FailureGuidance is { } failureGuidance)
            reply.FailureGuidance = Text(failureGuidance, 512);
        return reply;
    }

    private static RevisionInput MapRevision(ReviseFeatureDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draftId = new FeatureDraftId(Identifier(request.DraftId, 128));
        var expectedRevision = Revision(request.HasExpectedRevision, request.ExpectedRevision);
        var idempotencyId = Identifier(request.IdempotencyId, 256);
        RevisionCommand command = request.CommandCase switch
        {
            ReviseFeatureDraftRequest.CommandOneofCase.ReviseBehavior => new ReviseBehaviorCommand(
                ToDomain(request.ReviseBehavior?.Behavior)),
            ReviseFeatureDraftRequest.CommandOneofCase.ReviseSource => new ReviseSourceCommand(
                ToDomain(request.ReviseSource?.Source)),
            ReviseFeatureDraftRequest.CommandOneofCase.AcceptSuggestedChange => new AcceptPatchCommand(
                ToAcceptedPatch(request.AcceptSuggestedChange?.Patch, draftId)),
            ReviseFeatureDraftRequest.CommandOneofCase.RejectSuggestedChange => MapRejection(
                request.RejectSuggestedChange),
            _ => throw new ArgumentException("A typed Feature Draft revision command is required.")
        };
        return new RevisionInput(draftId, expectedRevision, idempotencyId, command);
    }

    private static RejectPatchCommand MapRejection(RejectSuggestedChangeInput? rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        return new RejectPatchCommand(
            Identifier(rejected.PatchId, 256),
            Revision(rejected.HasBaseRevision, rejected.BaseRevision));
    }

    private static DigitalBrain.Kernel.Contracts.FeatureDraftPatch ToAcceptedPatch(
        GrpcFeatureDraftPatch? patch,
        FeatureDraftId requestedDraftId)
    {
        var mapped = ToDomain(patch);
        if (mapped.DraftId != requestedDraftId)
            throw new ArgumentException("The Suggested Change must target the requested Feature Draft.");
        return mapped;
    }

    private static InstallFeatureVersion MapInstall(InstallFeatureVersionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Grants.Count > 32 || request.Subscriptions.Count > 64 ||
            request.Subscriptions.Count == 0 && request.Grants.Count != 0)
            throw new ArgumentException("The Feature installation collection bounds are invalid.");
        var grants = request.Grants.Select(ToDomain).ToArray();
        if (grants.Select(grant => grant.CapabilityId).Distinct(StringComparer.Ordinal).Count() != grants.Length)
            throw new ArgumentException("Feature capability grants must be unique.");
        var subscriptions = request.Subscriptions.Select(subscription => Identifier(subscription, 256)).ToArray();
        if (subscriptions.Distinct(StringComparer.Ordinal).Count() != subscriptions.Length)
            throw new ArgumentException("Feature subscriptions must be unique.");
        return new InstallFeatureVersion(
            new FeatureDraftId(Identifier(request.DraftId, 128)),
            Revision(request.HasExpectedRevision, request.ExpectedRevision),
            new FeatureInstallationId(Identifier(request.InstallationId, 256)),
            new ReleaseDigest(ReleaseDigest(request.ReleaseDigest)),
            grants,
            subscriptions,
            Identifier(request.DecisionId, 256),
            Identifier(request.IdempotencyId, 256));
    }

    private static PrepareFeatureAccessReview MapAccessReview(ReviewFeatureAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Grants.Count > 32 || request.Subscriptions.Count > 64 ||
            (request.Grants.Count == 0) != (request.Subscriptions.Count == 0))
            throw new ArgumentException("The Feature access review collection bounds are invalid.");
        var grants = request.Grants.Select(ToDomain).ToArray();
        if (grants.Select(grant => grant.CapabilityId).Distinct(StringComparer.Ordinal).Count() != grants.Length)
            throw new ArgumentException("Feature capability grants must be unique.");
        var subscriptions = request.Subscriptions.Select(subscription => Identifier(subscription, 256)).ToArray();
        if (subscriptions.Distinct(StringComparer.Ordinal).Count() != subscriptions.Length)
            throw new ArgumentException("Feature subscriptions must be unique.");
        return new PrepareFeatureAccessReview(
            new FeatureDraftId(Identifier(request.DraftId, 128)),
            Revision(request.HasExpectedRevision, request.ExpectedRevision),
            new FeatureInstallationId(Identifier(request.InstallationId, 256)),
            new ReleaseDigest(ReleaseDigest(request.ReleaseDigest)),
            grants,
            subscriptions);
    }

    private T MapRequest<T>(RuntimeRequestContext context, Func<T> mapping)
    {
        DemandRequestAuthority(context);
        try
        {
            return mapping();
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw Status(StatusCode.InvalidArgument, "The Feature request is invalid.");
        }
        catch (JsonException)
        {
            throw Status(StatusCode.InvalidArgument, "The Feature request is invalid.");
        }
        catch (Exception)
        {
            logger.LogError("A Feature request mapping failed safely.");
            throw Status(StatusCode.Internal, "The Feature request could not be completed.");
        }
    }

    private void DemandRequestAuthority(RuntimeRequestContext context)
    {
        try
        {
            FeatureSuggestionService.DemandFeatureAuthor(context);
        }
        catch (FeatureAuthorityRejectedException exception)
        {
            throw AuthorityStatus(exception.Reason);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("A Feature request authority check failed safely.");
            throw Status(StatusCode.Internal, "The Feature request could not be completed.");
        }
    }

    private async Task<T> InvokeAsync<T>(Func<Task<T>> invocation)
    {
        try
        {
            return await invocation().ConfigureAwait(false);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureAuthorityRejectedException exception)
        {
            throw AuthorityStatus(exception.Reason);
        }
        catch (KeyNotFoundException)
        {
            throw Status(StatusCode.NotFound, "The requested Feature resource was not found.");
        }
        catch (FeatureCommandRejectedException exception)
        {
            throw exception.Reason switch
            {
                FeatureCommandRejectionReason.Conflict => Status(
                    StatusCode.Aborted,
                    "The Feature Draft changed. Reload it and retry."),
                FeatureCommandRejectionReason.Precondition => Status(
                    StatusCode.FailedPrecondition,
                    "The Feature Draft is not ready for this operation."),
                FeatureCommandRejectionReason.Limit => Status(
                    StatusCode.ResourceExhausted,
                    "The Feature request exceeds a configured limit."),
                FeatureCommandRejectionReason.Unavailable => Status(
                    StatusCode.Unavailable,
                    "The Feature service is temporarily unavailable. Retry the same request."),
                _ => Status(StatusCode.Internal, "The Feature request could not be completed.")
            };
        }
        catch (TimeoutException)
        {
            throw Status(
                StatusCode.Unavailable,
                "The Feature service is temporarily unavailable. Retry the same request.");
        }
        catch (IOException)
        {
            throw Status(
                StatusCode.Unavailable,
                "The Feature service is temporarily unavailable. Retry the same request.");
        }
        catch (OrleansException)
        {
            throw Status(
                StatusCode.Unavailable,
                "The Feature service is temporarily unavailable. Retry the same request.");
        }
        catch (Exception)
        {
            logger.LogError("A Feature application invocation failed safely.");
            throw Status(StatusCode.Internal, "The Feature request could not be completed.");
        }
    }

    private T Project<T>(Func<T> projection)
    {
        try
        {
            return projection();
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError("A Feature response projection failed safely.");
            throw Status(StatusCode.Internal, "The Feature request could not be completed.");
        }
    }

    private static long Revision(bool present, long value)
    {
        if (!present || value < 0)
            throw new ArgumentException("A nonnegative Feature Draft Revision is required.");
        return value;
    }

    private static string Identifier(string value, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumCharacters || value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A canonical bounded identifier is required.");
        return value;
    }

    private static string Text(string value, int maximumCharacters) => Identifier(value, maximumCharacters);

    private static string ReleaseDigest(string value)
    {
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A canonical release digest is required.");
        return value;
    }

    private static string SourceReference(string value) =>
        CanonicalSourceReference(value)
            ? value
            : throw new ArgumentException("A canonical Feature source reference is required.");

    private static DigitalBrain.Kernel.Contracts.FeatureBehavior ToDomain(GrpcFeatureBehavior? behavior)
    {
        if (behavior is null || behavior.Scenarios.Count is 0 or > 32)
            throw new ArgumentException("A bounded Feature Behavior is required.");
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        var utf8Bytes = 0;
        var scenarios = behavior.Scenarios.Select(scenario =>
        {
            var scenarioId = Identifier(scenario.ScenarioId, 128);
            var name = Text(scenario.Name, 256);
            var given = Text(scenario.Given, 4096);
            var when = Text(scenario.When, 4096);
            var then = Text(scenario.Then, 4096);
            if (!scenarioIds.Add(scenarioId))
                throw new ArgumentException("Feature Scenario identifiers must be unique.");
            utf8Bytes = checked(utf8Bytes + Encoding.UTF8.GetByteCount(scenarioId) + Encoding.UTF8.GetByteCount(name) +
                Encoding.UTF8.GetByteCount(given) + Encoding.UTF8.GetByteCount(when) + Encoding.UTF8.GetByteCount(then));
            if (utf8Bytes > 65_536)
                throw new ArgumentException("Feature Behavior exceeds its UTF-8 bound.");
            return new DigitalBrain.Kernel.Contracts.FeatureScenario(scenarioId, name, given, when, then);
        }).ToArray();
        return new DigitalBrain.Kernel.Contracts.FeatureBehavior(scenarios);
    }

    private static DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot ToDomain(GrpcFeatureSourceSnapshot? source)
    {
        if (source is null || source.Files.Count is 0 or > 64)
            throw new ArgumentException("A bounded Feature Source is required.");
        var implementationProject = SourcePath(source.ImplementationProjectPath);
        var scenarioProject = SourcePath(source.ScenarioProjectPath);
        if (!implementationProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            !scenarioProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Feature Source projects must be C# projects.");
        var collisionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactPaths = new HashSet<string>(StringComparer.Ordinal);
        var aggregateBytes = 0;
        var files = source.Files.Select(file =>
        {
            var path = SourcePath(file.Path);
            if (!collisionPaths.Add(path))
                throw new ArgumentException("Feature Source paths must be unique.");
            exactPaths.Add(path);
            if (file.Content.Contains('\0', StringComparison.Ordinal))
                throw new ArgumentException("Feature Source content is invalid.");
            var contentBytes = Encoding.UTF8.GetByteCount(file.Content);
            if (contentBytes > 1_048_576)
                throw new ArgumentException("A Feature Source file exceeds its UTF-8 bound.");
            aggregateBytes = checked(aggregateBytes + contentBytes);
            if (aggregateBytes > 4_194_304)
                throw new ArgumentException("Feature Source exceeds its UTF-8 bound.");
            return new DigitalBrain.Kernel.Contracts.FeatureSourceFile(path, file.Content);
        }).ToArray();
        if (!exactPaths.Contains(implementationProject) || !exactPaths.Contains(scenarioProject))
            throw new ArgumentException("Feature Source projects must be present in the snapshot.");
        return new DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot(
            implementationProject,
            scenarioProject,
            files);
    }

    private static string SourcePath(string path)
    {
        if (path is null)
            throw new ArgumentException("A Feature Source path is required.");
        var segments = path.Split('/');
        if (path.Length is 0 or > 240 ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.StartsWith('/', StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' ||
            segments.Any(segment => !IsPortablePathSegment(segment)))
            throw new ArgumentException("A canonical portable Feature Source path is required.");
        return path;
    }

    private static bool IsPortablePathSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." ||
            !string.Equals(segment, segment.Trim(), StringComparison.Ordinal) ||
            segment.Any(char.IsControl) ||
            segment.IndexOfAny(InvalidSourcePathCharacters) >= 0 ||
            segment.EndsWith('.'))
            return false;
        return !ReservedSourcePathSegments.Contains(segment.Split('.', 2)[0]);
    }

    private static DigitalBrain.Kernel.Contracts.FeatureDraftPatch ToDomain(GrpcFeatureDraftPatch? patch)
    {
        if (patch is null || !patch.HasBaseRevision)
            throw new ArgumentException("A complete Suggested Change is required.");
        return new DigitalBrain.Kernel.Contracts.FeatureDraftPatch(
            Identifier(patch.PatchId, 256),
            new FeatureDraftId(Identifier(patch.DraftId, 128)),
            Revision(true, patch.BaseRevision),
            Text(patch.Summary, 2048),
            ToDomain(patch.ReplacementBehavior),
            ToDomain(patch.ReplacementSource));
    }

    private static FeatureGrantSpec ToDomain(GrpcFeatureGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var capabilityId = Identifier(grant.CapabilityId, 256);
        if (grant.CapabilityVersion < 1 || Encoding.UTF8.GetByteCount(grant.ConstraintsJson) > 65_536)
            throw new ArgumentException("A bounded Feature capability grant is required.");
        if (grant.HasConnectionId != grant.HasProvider)
            throw new ArgumentException("A Feature provider and connection must appear together.");
        ProviderConnectionId? connection = grant.HasConnectionId
            ? new ProviderConnectionId(Identifier(grant.ConnectionId, 256))
            : null;
        var provider = grant.HasProvider ? Identifier(grant.Provider, 64) : null;
        using var document = JsonDocument.Parse(grant.ConstraintsJson);
        var constraints = CapabilityGrantConstraintPolicy.CopyValidated(document.RootElement);
        if (!CapabilityGrantConstraintPolicy.AllowsTool(constraints, capabilityId))
            throw new ArgumentException("Feature capability constraints do not match the capability.");
        return new FeatureGrantSpec(
            capabilityId,
            grant.CapabilityVersion,
            connection,
            grant.ConstraintsJson,
            provider);
    }

    private static FeatureDraftReply ProjectDraft(FeatureDraftId expectedDraftId, FeatureDraftRecoverySnapshot read)
    {
        ArgumentNullException.ThrowIfNull(read);
        ValidateDraftOutput(expectedDraftId, read.Draft);
        var reply = new FeatureDraftReply { Draft = ToReply(read.Draft) };
        if (read.Recovery is { } recovery)
            reply.Recovery = ProjectRecovery(read.Draft, recovery);
        return reply;
    }

    private static FeatureInstallationRecovery ProjectRecovery(
        DigitalBrain.Kernel.Contracts.FeatureDraft draft,
        FeatureInstallationRecoverySnapshot recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        ArgumentNullException.ThrowIfNull(recovery.Verification);
        var verification = draft.Verification;
        var evidence = recovery.Verification.Evidence;
        var installed = string.Equals(draft.Status, "installed", StringComparison.Ordinal);
        ValidateReleaseOutput(recovery.Release);
        ValidateGrantCollection(recovery.Grants);
        ValidateSubscriptions(recovery.Subscriptions);
        if (verification is null || evidence is null ||
            !installed && !SameVerification(verification, recovery.Verification) ||
            recovery.Verification.Passed != recovery.Verification.Total ||
            recovery.Verification.Failed != 0 || recovery.Verification.Skipped != 0 ||
            recovery.Release.Source is not null ||
            recovery.Release.SourceKind != DigitalBrain.Kernel.Contracts.FeatureSourceKind.RuntimeAuthored ||
            recovery.Release.Digest != recovery.Verification.Release ||
            !string.Equals(recovery.Release.SourceReference, evidence.SourceReference, StringComparison.Ordinal) ||
            !SameIdentifiers(
                recovery.Release.RequestedCapabilities,
                recovery.Grants.Select(grant => grant.CapabilityId)))
            throw new InvalidDataException("Feature installation recovery coordinates are invalid.");
        if (recovery.Installed != installed ||
            installed && draft.InstallationId != recovery.InstallationId ||
            !installed && draft.InstallationId is not null)
            throw new InvalidDataException("Feature installation recovery state is invalid.");
        if (recovery.PreviousRelease is { } previous)
        {
            ValidateReleaseOutput(previous);
            if (previous.Source is not null || previous.Digest == recovery.Release.Digest)
                throw new InvalidDataException("Feature installation recovery previous release is invalid.");
        }
        if (installed)
        {
            if (recovery.DecisionId is not null || recovery.IdempotencyId is not null ||
                (recovery.PreviousRelease is not null) != recovery.RollbackAvailable ||
                recovery.RollbackAvailable && recovery.Paused ||
                recovery.Paused != (recovery.PauseReason is not null))
                throw new InvalidDataException("Installed Feature recovery state is invalid.");
        }
        else if (!BoundedIdentifier(recovery.DecisionId, 256) ||
                 !BoundedIdentifier(recovery.IdempotencyId, 256) ||
                 recovery.RollbackAvailable || recovery.Paused || recovery.PauseReason is not null)
        {
            throw new InvalidDataException("Reserved Feature recovery state is invalid.");
        }
        if (recovery.PauseReason is { } pauseReason && !BoundedText(pauseReason, 4096))
            throw new InvalidDataException("Feature installation recovery pause reason is invalid.");
        var reply = new FeatureInstallationRecovery
        {
            Installed = recovery.Installed,
            Verification = ToReply(evidence, recovery.Verification.Release, recovery.Verification.VerifiedAt),
            Release = ToMetadataReply(recovery.Release),
            InstallationId = Identifier(recovery.InstallationId.Value, 256),
            RollbackAvailable = recovery.RollbackAvailable,
            Paused = recovery.Paused
        };
        reply.Grants.Add(recovery.Grants.Select(ToReply));
        reply.Subscriptions.Add(recovery.Subscriptions.Select(subscription => Identifier(subscription, 256)));
        if (recovery.PreviousRelease is { } previousRelease)
            reply.PreviousRelease = ToMetadataReply(previousRelease);
        if (recovery.DecisionId is { } decisionId)
            reply.DecisionId = Identifier(decisionId, 256);
        if (recovery.IdempotencyId is { } idempotencyId)
            reply.IdempotencyId = Identifier(idempotencyId, 256);
        if (recovery.PauseReason is { } reason)
            reply.PauseReason = Text(reason, 4096);
        return reply;
    }

    private static FeatureDraftReply ProjectRevision(
        RevisionInput input,
        DigitalBrain.Kernel.Contracts.FeatureDraft draft)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateDraftOutput(input.DraftId, draft);
        var expectedRevision = input.Command is RejectPatchCommand
            ? input.ExpectedRevision
            : input.ExpectedRevision == long.MaxValue
                ? throw new InvalidDataException("Revised Feature Draft coordinates are invalid.")
                : input.ExpectedRevision + 1;
        var exactContent = input.Command switch
        {
            ReviseBehaviorCommand command => SameBehavior(draft.Behavior, command.Behavior),
            ReviseSourceCommand command => SameSource(draft.Source, command.Source),
            AcceptPatchCommand command =>
                command.Patch.DraftId == input.DraftId &&
                command.Patch.BaseRevision == input.ExpectedRevision &&
                SameBehavior(draft.Behavior, command.Patch.ReplacementBehavior) &&
                SameSource(draft.Source, command.Patch.ReplacementSource),
            RejectPatchCommand => true,
            _ => false
        };
        if (!string.Equals(draft.Status, "draft", StringComparison.Ordinal) ||
            draft.Revision != expectedRevision ||
            !exactContent)
            throw new InvalidDataException("Revised Feature Draft coordinates are invalid.");
        return new FeatureDraftReply { Draft = ToReply(draft) };
    }

    private static FeatureDraftPatchReply ProjectSuggestion(
        SuggestFeatureChange command,
        DigitalBrain.Kernel.Contracts.FeatureDraftPatch patch)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(patch);
        if (patch.DraftId != command.DraftId || patch.BaseRevision != command.ExpectedRevision)
            throw new InvalidDataException("Suggested Change coordinates are invalid.");
        return new FeatureDraftPatchReply { Patch = ToReply(patch) };
    }

    private static FeatureReleaseReviewReply ProjectVerification(
        VerifyFeatureDraft command,
        FeatureVerificationReview review)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(review);
        ValidateDraftOutput(command.DraftId, review.Draft);
        ValidateEvidenceOutput(review.Evidence);
        if (review.AttemptedAt.Offset != TimeSpan.Zero || review.AttemptedAt.ToUnixTimeMilliseconds() <= 0)
            throw new InvalidDataException("Feature Verification attempt time is invalid.");
        var passed = review.Evidence.Passed == review.Evidence.Total &&
                     review.Evidence.Failed == 0 && review.Evidence.Skipped == 0;
        if (!string.Equals(review.Draft.Status, "draft", StringComparison.Ordinal) || command.ExpectedRevision == long.MaxValue)
            throw new InvalidDataException("Verified Feature coordinates are invalid.");
        if (passed)
        {
            if (review.Release is not { } release ||
                review.Draft.Revision != command.ExpectedRevision + 1 ||
                review.Draft.Verification is not { } verification ||
                verification.Release != release.Digest ||
                verification.Total != review.Evidence.Total ||
                verification.Passed != review.Evidence.Passed ||
                verification.Failed != review.Evidence.Failed ||
                verification.Skipped != review.Evidence.Skipped ||
                verification.VerifiedAt != review.AttemptedAt ||
                release.SourceKind != DigitalBrain.Kernel.Contracts.FeatureSourceKind.RuntimeAuthored ||
                !string.Equals(release.SourceReference, review.Evidence.SourceReference, StringComparison.Ordinal))
                throw new InvalidDataException("Verified Feature coordinates are invalid.");
            ValidateReleaseOutput(release);
        }
        else if (review.Release is not null ||
                 review.Draft.Revision != command.ExpectedRevision &&
                 review.Draft.Revision != command.ExpectedRevision + 1)
        {
            throw new InvalidDataException("Failed Feature verification coordinates are invalid.");
        }
        var reply = new FeatureReleaseReviewReply
        {
            Draft = ToReply(review.Draft, false),
            Verification = ToReply(
                review.Evidence,
                review.Release?.Digest,
                review.AttemptedAt)
        };
        if (review.Release is { } verifiedRelease) reply.Release = ToMetadataReply(verifiedRelease);
        return reply;
    }

    private static FeatureAccessReviewReply ProjectAccessReview(
        PrepareFeatureAccessReview command,
        FeatureAccessReview review)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(review);
        ValidateDraftOutput(command.DraftId, review.Candidate.Draft);
        ValidateReleaseOutput(review.Candidate.Release);
        ValidateGrantCollection(review.Grants);
        ValidateSubscriptions(review.Subscriptions);
        var serverAuthoredPlan = command.Grants.Length == 0 && command.Subscriptions.Length == 0;
        if (review.Candidate.Draft.Revision != command.ExpectedRevision ||
            review.Candidate.Draft.Verification?.Release != command.Release ||
            review.Candidate.Release.Digest != command.Release ||
            review.Candidate.Release.Source is null ||
            review.InstallationId != command.InstallationId ||
            !serverAuthoredPlan && !SameGrants(review.Grants, command.Grants) ||
            !serverAuthoredPlan && !SameSubscriptions(review.Subscriptions, command.Subscriptions) ||
            review.PreviousRelease?.Digest == command.Release)
            throw new InvalidDataException("Feature access review coordinates are invalid.");
        var reply = new FeatureAccessReviewReply
        {
            Draft = ToReply(review.Candidate.Draft),
            Release = ToMetadataReply(review.Candidate.Release),
            InstallationId = Identifier(review.InstallationId.Value, 256)
        };
        reply.Grants.Add(review.Grants.Select(ToReply));
        reply.Subscriptions.Add(review.Subscriptions.Select(subscription => Identifier(subscription, 256)));
        if (review.PreviousRelease is { } previous)
        {
            ValidateReleaseOutput(previous);
            if (previous.Source is null)
                throw new InvalidDataException("The previous Feature release has no inspectable source.");
            reply.PreviousRelease = ToMetadataReply(previous);
        }
        return reply;
    }

    private static FeatureInstallReply ProjectInstallation(
        InstallFeatureVersion command,
        ActorId actorId,
        InstalledFeatureVersion installed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(installed);
        ValidateDraftOutput(command.DraftId, installed.Draft);
        ValidateReleaseOutput(installed.Release);
        ValidateGrantCollection(installed.Authority.ActiveGrants);
        ValidateSubscriptions(installed.Registration.Subscriptions);
        if (!string.Equals(installed.Draft.Status, "installed", StringComparison.Ordinal) ||
            command.ExpectedRevision == long.MaxValue ||
            installed.Draft.Revision != command.ExpectedRevision + 1 ||
            installed.Draft.InstallationId != command.InstallationId ||
            installed.Draft.Verification?.Release != command.Release ||
            installed.Release.Digest != command.Release ||
            installed.Registration.InstallationId != command.InstallationId ||
            installed.Registration.Release != command.Release ||
            installed.Authority.InstallationId != command.InstallationId ||
            installed.Authority.ActorId != actorId ||
            installed.Authority.ActiveRelease != command.Release ||
            installed.Authority.ActiveGrantRevision is null ||
            installed.Authority.PendingRelease is not null ||
            installed.Authority.PendingGrantRevision is not null ||
            installed.Authority.PendingGrants.Length != 0 ||
            installed.Release.SourceKind != DigitalBrain.Kernel.Contracts.FeatureSourceKind.RuntimeAuthored ||
            !SameIdentifiers(
                installed.Release.RequestedCapabilities,
                installed.Authority.ActiveGrants.Select(grant => grant.CapabilityId)) ||
            !SameGrants(installed.Authority.ActiveGrants, command.Grants) ||
            !SameSubscriptions(installed.Registration.Subscriptions, command.Subscriptions))
            throw new InvalidDataException("Installed Feature coordinates are invalid.");
        return ToReply(installed);
    }

    private static FeatureReply ProjectFeature(
        FeatureDraftId expectedDraftId,
        ActorId actorId,
        InstalledFeatureDetail detail)
    {
        ValidateInstalledFeatureDetail(expectedDraftId, actorId, detail);
        var draft = ToReply(detail.Draft);
        var reply = new FeatureReply
        {
            FeatureId = Identifier(expectedDraftId.Value, 128),
            OriginatingRequest = draft.OriginatingRequest,
            ActiveRelease = ToMetadataReply(detail.ActiveRelease),
            RollbackAvailable = detail.Authority.ExactRollbackAvailable,
            Paused = detail.Authority.Paused,
            InstallationId = Identifier(detail.Registration.InstallationId.Value, 256),
            Revision = detail.Revision >= 0
                ? detail.Revision
                : throw new InvalidDataException("Invalid Feature lifecycle revision.")
        };
        reply.ActiveGrants.Add(detail.Authority.ActiveGrants.Select(ToReply));
        reply.Subscriptions.Add(detail.Registration.Subscriptions.Select(subscription => Identifier(subscription, 256)));
        if (detail.PreviousRelease is { } previous)
        {
            ValidateReleaseOutput(previous);
            if (previous.Source is null)
                throw new InvalidDataException("The previous Feature release has no inspectable source.");
            reply.PreviousRelease = ToMetadataReply(previous);
        }
        if (detail.Authority.PauseReason is { } pauseReason)
            reply.PauseReason = Text(pauseReason, 4096);
        return reply;
    }

    private static FeatureReleaseSourceReply ProjectFeatureReleaseSource(
        FeatureDraftId expectedDraftId,
        FeatureInstallationId expectedInstallationId,
        ReleaseDigest expectedRelease,
        string expectedSourceReference,
        ActorId actorId,
        InstalledFeatureDetail detail)
    {
        ValidateInstalledFeatureDetail(expectedDraftId, actorId, detail);
        if (detail.Registration.InstallationId != expectedInstallationId)
            throw new InvalidDataException("Feature release source installation coordinates are invalid.");
        var release = detail.ActiveRelease.Digest == expectedRelease
            ? detail.ActiveRelease
            : detail.PreviousRelease?.Digest == expectedRelease
                ? detail.PreviousRelease
                : throw new InvalidDataException("Feature release source digest coordinates are invalid.");
        if (!string.Equals(release.SourceReference, expectedSourceReference, StringComparison.Ordinal) ||
            release.Source is not { } source)
            throw new InvalidDataException("Feature release source reference coordinates are invalid.");
        return new FeatureReleaseSourceReply
        {
            FeatureId = Identifier(expectedDraftId.Value, 128),
            InstallationId = Identifier(expectedInstallationId.Value, 256),
            ReleaseDigest = ReleaseDigest(expectedRelease.Value),
            SourceReference = CanonicalSourceReference(expectedSourceReference)
                ? expectedSourceReference
                : throw new InvalidDataException("Feature release source reference coordinates are invalid."),
            Source = ToReply(source)
        };
    }

    private static void ValidateInstalledFeatureDetail(
        FeatureDraftId expectedDraftId,
        ActorId actorId,
        InstalledFeatureDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ValidateDraftOutput(expectedDraftId, detail.Draft);
        ValidateReleaseOutput(detail.ActiveRelease);
        ValidateGrantCollection(detail.Authority.ActiveGrants);
        ValidateSubscriptions(detail.Registration.Subscriptions);
        if (!string.Equals(detail.Draft.Status, "installed", StringComparison.Ordinal) ||
            detail.Revision < 0 ||
            detail.Draft.InstallationId != detail.Registration.InstallationId ||
            detail.Authority.InstallationId != detail.Registration.InstallationId ||
            detail.Authority.ActorId != actorId ||
            detail.Authority.ActiveRelease != detail.ActiveRelease.Digest ||
            detail.Registration.Release != detail.ActiveRelease.Digest ||
            detail.ActiveRelease.Source is null ||
            !SameIdentifiers(
                detail.ActiveRelease.RequestedCapabilities,
                detail.Authority.ActiveGrants.Select(grant => grant.CapabilityId)) ||
            (detail.PreviousRelease is not null) != detail.Authority.ExactRollbackAvailable ||
            detail.Authority.ExactRollbackAvailable &&
            (detail.Authority.PreviousRelease is not { } previousDigest ||
             detail.PreviousRelease?.Digest != previousDigest))
            throw new InvalidDataException("Installed Feature detail coordinates are invalid.");
    }

    private static void ValidateDraftOutput(
        FeatureDraftId expectedDraftId,
        DigitalBrain.Kernel.Contracts.FeatureDraft draft)
    {
        ArgumentNullException.ThrowIfNull(expectedDraftId);
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.DraftId != expectedDraftId ||
            draft.Revision < 0 ||
            draft.CreatedAt.Offset != TimeSpan.Zero ||
            draft.UpdatedAt.Offset != TimeSpan.Zero ||
            draft.CreatedAt > draft.UpdatedAt)
            throw new InvalidDataException("Feature Draft output is invalid.");
        if (string.Equals(draft.Status, "draft", StringComparison.Ordinal) && draft.InstallationId is not null ||
            string.Equals(draft.Status, "installed", StringComparison.Ordinal) &&
            (draft.InstallationId is null || draft.Verification is null))
            throw new InvalidDataException("Feature Draft state is invalid.");
        if (draft.Verification is not { } verification)
            return;
        var resultCount = (long)verification.Passed + verification.Failed + verification.Skipped;
        if (verification.Total <= 0 ||
            verification.Passed < 0 ||
            verification.Failed < 0 ||
            verification.Skipped < 0 ||
            resultCount != verification.Total ||
            verification.VerifiedAt.Offset != TimeSpan.Zero ||
            verification.VerifiedAt < draft.CreatedAt ||
            verification.VerifiedAt > draft.UpdatedAt)
            throw new InvalidDataException("Feature Verification output is invalid.");
        if (string.Equals(draft.Status, "installed", StringComparison.Ordinal) &&
            (verification.Passed != verification.Total || verification.Failed != 0 || verification.Skipped != 0))
            throw new InvalidDataException("Installed Feature Verification output is invalid.");
        if (verification.Evidence is { } evidence)
        {
            ValidateEvidenceOutput(evidence);
            if (evidence.Total != verification.Total || evidence.Passed != verification.Passed ||
                evidence.Failed != verification.Failed || evidence.Skipped != verification.Skipped)
                throw new InvalidDataException("Feature Verification evidence totals are inconsistent.");
        }
        _ = ReleaseDigest(verification.Release.Value);
    }

    private static void ValidateEvidenceOutput(FeatureVerificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.Scenarios);
        ArgumentNullException.ThrowIfNull(evidence.Artifacts);
        if (!CanonicalSourceReference(evidence.SourceReference) ||
            evidence.Total is <= 0 or > 1024 || evidence.Passed < 0 || evidence.Failed < 0 || evidence.Skipped < 0 ||
            (long)evidence.Passed + evidence.Failed + evidence.Skipped != evidence.Total ||
            evidence.Scenarios.Length != evidence.Total)
            throw new InvalidDataException("Feature Verification evidence is invalid.");
        long utf8Bytes = Encoding.UTF8.GetByteCount(evidence.SourceReference);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var scenario in evidence.Scenarios)
        {
            if (!identifiers.Add(Identifier(scenario.ScenarioId, 256)) ||
                string.IsNullOrWhiteSpace(Text(scenario.Name, 512)) ||
                !Enum.IsDefined(scenario.Outcome) || scenario.DurationMilliseconds is < 0 or > 70_000)
                throw new InvalidDataException("Feature scenario evidence is invalid.");
            switch (scenario.Outcome)
            {
                case DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Passed:
                    passed++;
                    if (scenario.SafeFailure is not null)
                        throw new InvalidDataException("Passing Feature scenario evidence cannot contain a failure.");
                    break;
                case DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Failed:
                    failed++;
                    _ = Text(scenario.SafeFailure ?? string.Empty, 4096);
                    break;
                case DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Skipped:
                    skipped++;
                    if (scenario.SafeFailure is { } skippedReason)
                        _ = Text(skippedReason, 4096);
                    break;
                default:
                    throw new InvalidDataException("Feature scenario evidence has an unknown outcome.");
            }
            utf8Bytes = checked(utf8Bytes +
                Encoding.UTF8.GetByteCount(scenario.ScenarioId) +
                Encoding.UTF8.GetByteCount(scenario.Name) +
                Encoding.UTF8.GetByteCount(scenario.SafeFailure ?? string.Empty));
        }
        if (passed != evidence.Passed || failed != evidence.Failed || skipped != evidence.Skipped)
            throw new InvalidDataException("Feature scenario evidence totals are inconsistent.");
        if (evidence.Artifacts.Length > 32 ||
            evidence.Artifacts.Select(artifact => artifact.Name).Distinct(StringComparer.Ordinal).Count() != evidence.Artifacts.Length)
            throw new InvalidDataException("Feature Verification artifacts are invalid.");
        foreach (var artifact in evidence.Artifacts)
        {
            _ = Identifier(artifact.Name, 256);
            _ = Identifier(artifact.MediaType, 128);
            if (artifact.SizeBytes is < 0 or > 1_048_576 || !CanonicalSourceReference(artifact.Digest))
                throw new InvalidDataException("Feature Verification artifact coordinates are invalid.");
            utf8Bytes = checked(utf8Bytes +
                Encoding.UTF8.GetByteCount(artifact.Name) +
                Encoding.UTF8.GetByteCount(artifact.MediaType) +
                Encoding.UTF8.GetByteCount(artifact.Digest));
        }
        if (utf8Bytes > MaximumVerificationEvidenceUtf8Bytes)
            throw new InvalidDataException("Feature Verification evidence exceeds its UTF-8 byte budget.");
    }

    private static void ValidateReleaseOutput(FeatureReleaseMetadata release)
    {
        ArgumentNullException.ThrowIfNull(release);
        _ = ReleaseDigest(release.Digest.Value);
        ValidateIdentifierList(release.RequestedCapabilities, 64);
        ValidateIdentifierList(release.Dependencies, 64);
        if (!Enum.IsDefined(release.SourceKind))
            throw new InvalidDataException("Feature release source kind is invalid.");
        if (!CanonicalSourceReference(release.SourceReference))
            throw new InvalidDataException("Feature release source reference is invalid.");
    }

    private static bool CanonicalSourceReference(string value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) && CanonicalDigest(value[7..]);

    private static bool CanonicalDigest(string value) =>
        value is { Length: 64 } && !value.Any(character =>
            character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'));

    private static void ValidateIdentifierList(string[] values, int maximumCount)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length > maximumCount || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("Feature release identifiers are invalid.");
        foreach (var value in values)
            _ = Identifier(value, 256);
    }

    private static void ValidateGrantCollection(FeatureGrantSpec[] grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (grants.Length > 32 ||
            grants.Select(grant => grant.CapabilityId).Distinct(StringComparer.Ordinal).Count() != grants.Length)
            throw new InvalidDataException("Feature grant collection is invalid.");
        foreach (var grant in grants)
            _ = ToReply(grant);
    }

    private static void ValidateSubscriptions(string[] subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        if (subscriptions.Length is 0 or > 64 ||
            subscriptions.Distinct(StringComparer.Ordinal).Count() != subscriptions.Length)
            throw new InvalidDataException("Feature subscriptions are invalid.");
        foreach (var subscription in subscriptions)
            _ = Identifier(subscription, 256);
    }

    private static bool SameGrants(IReadOnlyList<FeatureGrantSpec> left, IReadOnlyList<FeatureGrantSpec> right) =>
        left.OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal)
            .ThenBy(grant => grant.CapabilityVersion)
            .SequenceEqual(
                right.OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal)
                    .ThenBy(grant => grant.CapabilityVersion));

    private static bool SameSubscriptions(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool SameVerification(
        DigitalBrain.Kernel.Contracts.FeatureVerification left,
        DigitalBrain.Kernel.Contracts.FeatureVerification right) =>
        left.Release == right.Release && left.Total == right.Total && left.Passed == right.Passed &&
        left.Failed == right.Failed && left.Skipped == right.Skipped && left.VerifiedAt == right.VerifiedAt &&
        SameEvidence(left.Evidence, right.Evidence);

    private static bool SameEvidence(FeatureVerificationEvidence? left, FeatureVerificationEvidence? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        string.Equals(left.SourceReference, right.SourceReference, StringComparison.Ordinal) &&
        left.Total == right.Total && left.Passed == right.Passed && left.Failed == right.Failed &&
        left.Skipped == right.Skipped && left.Scenarios.SequenceEqual(right.Scenarios) &&
        left.Artifacts.SequenceEqual(right.Artifacts);

    private static bool BoundedIdentifier(string? value, int maximumLength) =>
        value is not null && BoundedText(value, maximumLength);

    private static bool BoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) && !value.Any(char.IsControl);

    private static bool SameBehavior(
        DigitalBrain.Kernel.Contracts.FeatureBehavior left,
        DigitalBrain.Kernel.Contracts.FeatureBehavior right) =>
        left.Scenarios.Length == right.Scenarios.Length &&
        left.Scenarios.Zip(right.Scenarios).All(pair =>
            string.Equals(pair.First.ScenarioId, pair.Second.ScenarioId, StringComparison.Ordinal) &&
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
            string.Equals(pair.First.Given, pair.Second.Given, StringComparison.Ordinal) &&
            string.Equals(pair.First.When, pair.Second.When, StringComparison.Ordinal) &&
            string.Equals(pair.First.Then, pair.Second.Then, StringComparison.Ordinal));

    private static bool SameSource(
        DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot left,
        DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot right) =>
        string.Equals(left.ImplementationProjectPath, right.ImplementationProjectPath, StringComparison.Ordinal) &&
        string.Equals(left.ScenarioProjectPath, right.ScenarioProjectPath, StringComparison.Ordinal) &&
        left.Files.Length == right.Files.Length &&
        left.Files.Zip(right.Files).All(pair =>
            string.Equals(pair.First.Path, pair.Second.Path, StringComparison.Ordinal) &&
            string.Equals(pair.First.Content, pair.Second.Content, StringComparison.Ordinal));

    private static bool SameIdentifiers(IEnumerable<string> left, IEnumerable<string> right) =>
        left.Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static GrpcFeatureDraft ToReply(DigitalBrain.Kernel.Contracts.FeatureDraft draft)
    {
        return ToReply(draft, true);
    }

    private static GrpcFeatureDraft ToReply(
        DigitalBrain.Kernel.Contracts.FeatureDraft draft,
        bool includeSource)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var originatingRequest = new GrpcOriginatingRequest
        {
            OperationId = Identifier(draft.OriginatingRequest.OperationId, 256),
            Text = Text(draft.OriginatingRequest.Text, 4096)
        };
        if (!string.Equals(
                draft.OriginatingRequest.ConversationId,
                DigitalBrain.Kernel.Contracts.FeatureDraft.LegacyMissingConversationId,
                StringComparison.Ordinal))
            originatingRequest.ConversationId = Identifier(draft.OriginatingRequest.ConversationId, 256);
        var reply = new GrpcFeatureDraft
        {
            DraftId = Identifier(draft.DraftId.Value, 128),
            OriginatingRequest = originatingRequest,
            Goal = Text(draft.Goal, 4096),
            Status = draft.Status switch
            {
                "draft" => GrpcFeatureDraftStatus.Draft,
                "installed" => GrpcFeatureDraftStatus.Installed,
                _ => throw new InvalidDataException("Unknown Feature Draft status.")
            },
            Behavior = ToReply(draft.Behavior),
            Revision = draft.Revision >= 0 ? draft.Revision : throw new InvalidDataException("Invalid Feature Draft revision."),
            CreatedAtUnixMs = draft.CreatedAt.ToUnixTimeMilliseconds(),
            UpdatedAtUnixMs = draft.UpdatedAt.ToUnixTimeMilliseconds()
        };
        if (includeSource)
            reply.Source = ToReply(draft.Source);
        if (draft.Verification is { } verification)
        {
            reply.Verification = new DigitalBrain.V2.Ui.Grpc.FeatureVerification
            {
                ReleaseDigest = ReleaseDigest(verification.Release.Value),
                Total = verification.Total,
                Passed = verification.Passed,
                Failed = verification.Failed,
                Skipped = verification.Skipped,
                VerifiedAtUnixMs = verification.VerifiedAt.ToUnixTimeMilliseconds()
            };
            if (verification.Evidence is { } evidence)
                reply.Verification.SourceReference = SourceReference(evidence.SourceReference);
        }
        if (draft.InstallationId is { } installationId)
            reply.InstallationId = Identifier(installationId.Value, 256);
        return reply;
    }

    private static GrpcFeatureBehavior ToReply(DigitalBrain.Kernel.Contracts.FeatureBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        var mapped = ToDomain(new GrpcFeatureBehavior
        {
            Scenarios =
            {
                behavior.Scenarios.Select(scenario => new GrpcFeatureScenario
                {
                    ScenarioId = scenario.ScenarioId,
                    Name = scenario.Name,
                    Given = scenario.Given,
                    When = scenario.When,
                    Then = scenario.Then
                })
            }
        });
        var reply = new GrpcFeatureBehavior();
        reply.Scenarios.Add(mapped.Scenarios.Select(scenario => new GrpcFeatureScenario
        {
            ScenarioId = scenario.ScenarioId,
            Name = scenario.Name,
            Given = scenario.Given,
            When = scenario.When,
            Then = scenario.Then
        }));
        return reply;
    }

    private static GrpcFeatureSourceSnapshot ToReply(DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var candidate = new GrpcFeatureSourceSnapshot
        {
            ImplementationProjectPath = source.ImplementationProjectPath,
            ScenarioProjectPath = source.ScenarioProjectPath
        };
        candidate.Files.Add(source.Files.Select(file => new GrpcFeatureSourceFile
        {
            Path = file.Path,
            Content = file.Content
        }));
        var mapped = ToDomain(candidate);
        var reply = new GrpcFeatureSourceSnapshot
        {
            ImplementationProjectPath = mapped.ImplementationProjectPath,
            ScenarioProjectPath = mapped.ScenarioProjectPath
        };
        reply.Files.Add(mapped.Files.Select(file => new GrpcFeatureSourceFile
        {
            Path = file.Path,
            Content = file.Content
        }));
        return reply;
    }

    private static GrpcFeatureDraftPatch ToReply(DigitalBrain.Kernel.Contracts.FeatureDraftPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var reply = new GrpcFeatureDraftPatch
        {
            PatchId = Identifier(patch.PatchId, 256),
            DraftId = Identifier(patch.DraftId.Value, 128),
            BaseRevision = patch.BaseRevision >= 0 ? patch.BaseRevision : throw new InvalidDataException("Invalid Suggested Change revision."),
            Summary = Text(patch.Summary, 2048),
            ReplacementBehavior = ToReply(patch.ReplacementBehavior),
            ReplacementSource = ToReply(patch.ReplacementSource)
        };
        return reply;
    }

    private static GrpcFeatureRelease ToReply(FeatureReleaseMetadata release)
    {
        return ToReply(release, true);
    }

    private static GrpcFeatureRelease ToMetadataReply(FeatureReleaseMetadata release)
    {
        return ToReply(release, false);
    }

    private static GrpcFeatureRelease ToReply(FeatureReleaseMetadata release, bool includeSource)
    {
        ValidateReleaseOutput(release);
        var reply = new GrpcFeatureRelease
        {
            Digest = ReleaseDigest(release.Digest.Value),
            SourceKind = release.SourceKind switch
            {
                DigitalBrain.Kernel.Contracts.FeatureSourceKind.Repository => GrpcFeatureSourceKind.Repository,
                DigitalBrain.Kernel.Contracts.FeatureSourceKind.RuntimeAuthored => GrpcFeatureSourceKind.RuntimeAuthored,
                _ => throw new InvalidDataException("Unknown Feature Source kind.")
            },
            SourceReference = release.SourceReference
        };
        reply.RequestedCapabilityIds.Add(release.RequestedCapabilities.Select(capability => Identifier(capability, 256)));
        reply.Dependencies.Add(release.Dependencies.Select(dependency => Identifier(dependency, 256)));
        if (includeSource && release.Source is { } source) reply.Source = ToReply(source);
        return reply;
    }

    private static DigitalBrain.V2.Ui.Grpc.FeatureVerification ToReply(
        FeatureVerificationEvidence evidence,
        ReleaseDigest? release,
        DateTimeOffset? verifiedAt)
    {
        ValidateEvidenceOutput(evidence);
        var reply = new DigitalBrain.V2.Ui.Grpc.FeatureVerification
        {
            ReleaseDigest = release is { } digest ? ReleaseDigest(digest.Value) : string.Empty,
            Total = evidence.Total,
            Passed = evidence.Passed,
            Failed = evidence.Failed,
            Skipped = evidence.Skipped,
            VerifiedAtUnixMs = verifiedAt?.ToUnixTimeMilliseconds() ?? 0,
            SourceReference = evidence.SourceReference
        };
        reply.Scenarios.Add(evidence.Scenarios.Select(scenario =>
        {
            var mapped = new FeatureVerificationScenario
            {
                ScenarioId = scenario.ScenarioId,
                Name = scenario.Name,
                Outcome = scenario.Outcome switch
                {
                    DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Passed => DigitalBrain.V2.Ui.Grpc.FeatureScenarioOutcome.Passed,
                    DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Failed => DigitalBrain.V2.Ui.Grpc.FeatureScenarioOutcome.Failed,
                    DigitalBrain.Kernel.Contracts.FeatureScenarioOutcome.Skipped => DigitalBrain.V2.Ui.Grpc.FeatureScenarioOutcome.Skipped,
                    _ => throw new InvalidDataException("Unknown Feature scenario outcome.")
                },
                DurationMilliseconds = scenario.DurationMilliseconds
            };
            if (scenario.SafeFailure is { } safeFailure) mapped.SafeFailure = safeFailure;
            return mapped;
        }));
        reply.Artifacts.Add(evidence.Artifacts.Select(artifact => new DigitalBrain.V2.Ui.Grpc.FeatureVerificationArtifact
        {
            Name = artifact.Name,
            MediaType = artifact.MediaType,
            SizeBytes = artifact.SizeBytes,
            Digest = artifact.Digest
        }));
        return reply;
    }

    private static GrpcFeatureGrant ToReply(FeatureGrantSpec grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (Encoding.UTF8.GetByteCount(grant.ConstraintsJson) > 65_536)
            throw new InvalidDataException("Feature grant constraints exceed their bound.");
        using var document = JsonDocument.Parse(grant.ConstraintsJson);
        var constraints = CapabilityGrantConstraintPolicy.CopyValidated(document.RootElement);
        if (!CapabilityGrantConstraintPolicy.AllowsTool(constraints, grant.CapabilityId))
            throw new InvalidDataException("Feature grant constraints do not match their capability.");
        var reply = new GrpcFeatureGrant
        {
            CapabilityId = Identifier(grant.CapabilityId, 256),
            CapabilityVersion = grant.CapabilityVersion >= 1
                ? grant.CapabilityVersion
                : throw new InvalidDataException("Invalid Feature capability version."),
            ConstraintsJson = grant.ConstraintsJson
        };
        if ((grant.ProviderConnectionId is null) != (grant.Provider is null))
            throw new InvalidDataException("Feature grant provider authority is incomplete.");
        if (grant.ProviderConnectionId is { } providerConnectionId)
            reply.ConnectionId = Identifier(providerConnectionId.Value, 256);
        if (grant.Provider is { } provider)
            reply.Provider = Identifier(provider, 64);
        return reply;
    }

    private static FeatureInstallReply ToReply(InstalledFeatureVersion installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var reply = new FeatureInstallReply
        {
            Draft = ToReply(installed.Draft),
            Release = ToMetadataReply(installed.Release),
            InstallationId = Identifier(installed.Registration.InstallationId.Value, 256),
            RollbackAvailable = installed.Authority.ExactRollbackAvailable,
            Paused = installed.Authority.Paused
        };
        reply.ActiveGrants.Add(installed.Authority.ActiveGrants.Select(ToReply));
        if (installed.Registration.Subscriptions.Length is 0 or > 64)
            throw new InvalidDataException("Invalid Feature subscriptions.");
        reply.Subscriptions.Add(installed.Registration.Subscriptions.Select(subscription => Identifier(subscription, 256)));
        if (installed.Authority.PauseReason is { } pauseReason)
            reply.PauseReason = Text(pauseReason, 4096);
        return reply;
    }

    private static RpcException Status(StatusCode code, string detail) => new(new Status(code, detail));

    private static RpcException AuthorityStatus(FeatureAuthorityRejectionReason reason) => reason switch
    {
        FeatureAuthorityRejectionReason.MissingGrant => Status(
            StatusCode.PermissionDenied,
            "Feature management authority is required."),
        FeatureAuthorityRejectionReason.ActorMismatch => Status(
            StatusCode.FailedPrecondition,
            "The Feature Draft is not ready for this operation."),
        _ => Status(StatusCode.Internal, "The Feature request could not be completed.")
    };

    private sealed record RevisionInput(
        FeatureDraftId DraftId,
        long ExpectedRevision,
        string IdempotencyId,
        RevisionCommand Command);

    private sealed record FeatureReleaseSourceCoordinate(
        FeatureDraftId DraftId,
        FeatureInstallationId InstallationId,
        ReleaseDigest Release,
        string SourceReference);

    private sealed record ResumeOriginatingRequestInput(
        FeatureDraftId DraftId,
        long ExpectedRevision,
        string IdempotencyId);

    private sealed record ActivityQueryInput(
        DigitalBrain.Kernel.Contracts.FeatureRunStatus? Status,
        DigitalBrain.Kernel.Contracts.FeatureRunOrigin? Origin,
        FeatureDraftId? FeatureId,
        int Limit);

    private abstract record RevisionCommand;
    private sealed record ReviseBehaviorCommand(DigitalBrain.Kernel.Contracts.FeatureBehavior Behavior) : RevisionCommand;
    private sealed record ReviseSourceCommand(DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot Source) : RevisionCommand;
    private sealed record AcceptPatchCommand(DigitalBrain.Kernel.Contracts.FeatureDraftPatch Patch) : RevisionCommand;
    private sealed record RejectPatchCommand(string PatchId, long BaseRevision) : RevisionCommand;
}
