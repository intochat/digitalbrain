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
using DigitalBrain.V2.Ui.Grpc;

namespace DigitalBrain.Mcp;

public sealed class DigitalBrainUiEndpoints(
    FeatureAuthoringService authoring,
    FeatureSuggestionService suggestions,
    ILogger<DigitalBrainUiEndpoints> logger)
{
    private static readonly char[] InvalidSourcePathCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly HashSet<string> ReservedSourcePathSegments = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<FeatureDraftReply> GetFeatureDraftAsync(
        RuntimeRequestContext context,
        GetFeatureDraftRequest request,
        CancellationToken cancellationToken)
    {
        var draftId = MapRequest(context, () => new FeatureDraftId(Identifier(request.DraftId, 128)));
        var draft = await InvokeAsync(() => authoring.ReadAsync(context, draftId, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectDraft(draftId, draft));
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
        var candidate = await InvokeAsync(() => authoring.VerifyAsync(context, command, cancellationToken)).ConfigureAwait(false);
        return Project(() => ProjectVerification(command, candidate));
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
        if (request.Grants.Count > 32 || request.Subscriptions.Count is 0 or > 64)
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

    private static FeatureDraftReply ProjectDraft(FeatureDraftId expectedDraftId, DigitalBrain.Kernel.Contracts.FeatureDraft draft)
    {
        ValidateDraftOutput(expectedDraftId, draft);
        return new FeatureDraftReply { Draft = ToReply(draft) };
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
        VerifiedFeatureCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateDraftOutput(command.DraftId, candidate.Draft);
        ValidateReleaseOutput(candidate.Release);
        if (!string.Equals(candidate.Draft.Status, "draft", StringComparison.Ordinal) ||
            command.ExpectedRevision == long.MaxValue ||
            candidate.Draft.Revision != command.ExpectedRevision + 1 ||
            candidate.Draft.Verification is not { } verification ||
            verification.Total <= 0 ||
            verification.Passed != verification.Total ||
            verification.Failed != 0 ||
            verification.Skipped != 0 ||
            verification.Release != candidate.Release.Digest ||
            candidate.Release.SourceKind != DigitalBrain.Kernel.Contracts.FeatureSourceKind.RuntimeAuthored)
            throw new InvalidDataException("Verified Feature coordinates are invalid.");
        return new FeatureReleaseReviewReply
        {
            Draft = ToReply(candidate.Draft),
            Release = ToReply(candidate.Release)
        };
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
        _ = ReleaseDigest(verification.Release.Value);
    }

    private static void ValidateReleaseOutput(FeatureReleaseMetadata release)
    {
        ArgumentNullException.ThrowIfNull(release);
        _ = ReleaseDigest(release.Digest.Value);
        ValidateIdentifierList(release.RequestedCapabilities, 64);
        ValidateIdentifierList(release.Dependencies, 64);
        if (!Enum.IsDefined(release.SourceKind))
            throw new InvalidDataException("Feature release source kind is invalid.");
    }

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
            Source = ToReply(draft.Source),
            Revision = draft.Revision >= 0 ? draft.Revision : throw new InvalidDataException("Invalid Feature Draft revision."),
            CreatedAtUnixMs = draft.CreatedAt.ToUnixTimeMilliseconds(),
            UpdatedAtUnixMs = draft.UpdatedAt.ToUnixTimeMilliseconds()
        };
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
        ValidateReleaseOutput(release);
        var reply = new GrpcFeatureRelease
        {
            Digest = ReleaseDigest(release.Digest.Value),
            SourceKind = release.SourceKind switch
            {
                DigitalBrain.Kernel.Contracts.FeatureSourceKind.Repository => GrpcFeatureSourceKind.Repository,
                DigitalBrain.Kernel.Contracts.FeatureSourceKind.RuntimeAuthored => GrpcFeatureSourceKind.RuntimeAuthored,
                _ => throw new InvalidDataException("Unknown Feature Source kind.")
            }
        };
        reply.RequestedCapabilityIds.Add(release.RequestedCapabilities.Select(capability => Identifier(capability, 256)));
        reply.Dependencies.Add(release.Dependencies.Select(dependency => Identifier(dependency, 256)));
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
            Release = ToReply(installed.Release),
            InstallationId = Identifier(installed.Registration.InstallationId.Value, 256),
            RollbackAvailable = installed.Authority.PreviousRelease is not null,
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

    private abstract record RevisionCommand;
    private sealed record ReviseBehaviorCommand(DigitalBrain.Kernel.Contracts.FeatureBehavior Behavior) : RevisionCommand;
    private sealed record ReviseSourceCommand(DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot Source) : RevisionCommand;
    private sealed record AcceptPatchCommand(DigitalBrain.Kernel.Contracts.FeatureDraftPatch Patch) : RevisionCommand;
    private sealed record RejectPatchCommand(string PatchId, long BaseRevision) : RevisionCommand;
}
