using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureDraftAuthoringTransitions
{
    private static readonly char[] InvalidSourcePathCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly HashSet<string> ReservedSourcePathSegments = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"],
        StringComparer.OrdinalIgnoreCase);

    public static FeatureDraft? ReadDraft(FeatureHubState state, FeatureDraftId draftId)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandDraftId(draftId);
        return (state.Drafts ?? []).FirstOrDefault(draft => draft.DraftId == draftId);
    }

    public static FeatureDraftInstallationReservation? ReadInstallationReservation(FeatureHubState state, FeatureDraftId draftId)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandDraftId(draftId);
        return (state.DraftInstallationReservations ?? []).SingleOrDefault(candidate => candidate.DraftId == draftId);
    }

    public static FeatureDraftInstallationReservationTransition AcquireInstallationReservation(
        FeatureHubState state,
        InstallFeatureVersion command,
        ActorId actorId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        if (command.ExpectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(command));
        DemandText(command.InstallationId.Value, 256, nameof(command.InstallationId));
        DemandRelease(command.Release);
        DemandText(command.DecisionId, 256, nameof(command.DecisionId));
        DemandText(command.IdempotencyId, 256, nameof(command.IdempotencyId));
        DemandText(actorId.Value, 256, nameof(actorId));
        var grants = FeatureHubTransitions.ValidateGrants(command.Grants);
        DemandSubscriptions(command.Subscriptions);
        var subscriptions = command.Subscriptions.Order(StringComparer.Ordinal).ToArray();
        var canonicalCommand = command with
        {
            Grants = grants.Select(grant => new FeatureGrantSpec(
                grant.CapabilityId,
                grant.CapabilityVersion,
                grant.ProviderConnectionId,
                grant.ConstraintsJson,
                grant.Provider)).ToArray(),
            Subscriptions = subscriptions
        };
        var commandDigest = Fingerprint(canonicalCommand);
        var accessDigest = FeaturePublicationTransitions.AccessDigest(
            command.InstallationId,
            command.Release,
            grants,
            subscriptions);
        var reservations = (state.DraftInstallationReservations ?? []).ToArray();
        if ((state.Drafts ?? []).Any(candidate =>
                candidate.DraftId != command.DraftId &&
                candidate.InstallationId is { } installationId &&
                (installationId == command.InstallationId || candidate.Verification?.Release == command.Release)))
            throw new FeatureConcurrencyException("The Feature installation coordinate is already bound to another Draft.");
        var existing = reservations.SingleOrDefault(candidate => candidate.DraftId == command.DraftId);
        if (existing is not null)
        {
            if (existing.DraftRevision != command.ExpectedRevision ||
                existing.InstallationId != command.InstallationId ||
                existing.Release != command.Release ||
                existing.ActorId != actorId ||
                !string.Equals(existing.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal) ||
                !string.Equals(existing.CommandDigest, commandDigest, StringComparison.Ordinal))
                throw new FeatureConcurrencyException("The Feature Draft is reserved for a different installation command.");
            return new FeatureDraftInstallationReservationTransition(state, existing);
        }
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        var verification = draft.Verification
            ?? throw new FeatureConcurrencyException(
                "The Feature Draft has no Verification to reserve for installation.",
                FeatureCommandRejectionReason.Precondition);
        if (verification.Release != command.Release || verification.Total <= 0 ||
            verification.Passed != verification.Total || verification.Failed != 0 || verification.Skipped != 0)
            throw new FeatureConcurrencyException(
                "Only the exact fully verified Feature release can be reserved for installation.",
                FeatureCommandRejectionReason.Precondition);
        if (draft.InstallationId is { } installationId && installationId != command.InstallationId)
            throw new FeatureConcurrencyException(
                "The Feature Draft is bound to another installation identity.",
                FeatureCommandRejectionReason.Precondition);
        if (reservations.Any(candidate =>
                candidate.InstallationId == command.InstallationId ||
                candidate.Release == command.Release && candidate.InstallationId != command.InstallationId))
            throw new FeatureConcurrencyException("The Feature installation coordinate is already reserved.");
        if (reservations.Length >= FeatureLimits.DraftInstallationReservations)
            throw new FeatureLimitExceededException("An Owner can have at most 100 active Feature installation reservations.");
        var reservation = new FeatureDraftInstallationReservation(
            command.DraftId,
            command.ExpectedRevision,
            command.InstallationId,
            command.Release,
            command.IdempotencyId,
            commandDigest,
            accessDigest,
            command.DecisionId,
            actorId);
        return new FeatureDraftInstallationReservationTransition(
            state with
            {
                DraftInstallationReservations = [.. reservations, reservation],
                Revision = NextRevision(state.Revision)
            },
            reservation);
    }

    public static FeatureDraftAuthoringTransition ReviseBehavior(FeatureHubState state, ReviseFeatureBehavior command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.RevisedAt);
        var behavior = ValidateBehavior(command.Behavior);
        var digest = Fingerprint(command with { RevisedAt = default });
        if (Replay(
                state,
                command.DraftId,
                command.IdempotencyId,
                "behavior",
                digest,
                at => Fingerprint(command with { RevisedAt = at })) is { } replay)
            return replay;
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        return Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                draft.Status,
                behavior,
                draft.Source,
                null,
                draft.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                command.RevisedAt),
            command.IdempotencyId,
            "behavior",
            digest);
    }

    public static FeatureDraftAuthoringTransition ReviseSource(FeatureHubState state, ReviseFeatureSource command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.RevisedAt);
        var source = ValidateSource(command.Source);
        var digest = Fingerprint(command with { RevisedAt = default });
        if (Replay(
                state,
                command.DraftId,
                command.IdempotencyId,
                "source",
                digest,
                at => Fingerprint(command with { RevisedAt = at })) is { } replay)
            return replay;
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        return Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                draft.Status,
                draft.Behavior,
                source,
                null,
                draft.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                command.RevisedAt),
            command.IdempotencyId,
            "source",
            digest);
    }

    public static FeatureDraftAuthoringTransition AcceptSuggestedChange(FeatureHubState state, AcceptSuggestedChange command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Patch);
        var patch = ValidatePatch(command.Patch);
        DemandMutation(command.IdempotencyId, command.AcceptedAt);
        if (patch.BaseRevision != command.ExpectedRevision)
            throw new FeatureConcurrencyException("The Suggested Change does not target the expected Draft Revision.");
        var digest = Fingerprint(command with { AcceptedAt = default });
        if (Replay(
                state,
                patch.DraftId,
                command.IdempotencyId,
                "suggested-change",
                digest,
                at => Fingerprint(command with { AcceptedAt = at })) is { } replay)
            return replay;
        var draft = DemandEditableDraft(state, patch.DraftId, command.ExpectedRevision);
        return Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                draft.Status,
                patch.ReplacementBehavior,
                patch.ReplacementSource,
                null,
                draft.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                command.AcceptedAt),
            command.IdempotencyId,
            "suggested-change",
            digest);
    }

    public static FeatureDraftAuthoringTransition RejectSuggestedChange(FeatureHubState state, RejectSuggestedChange command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandText(command.PatchId, FeatureLimits.DraftPatchIdCharacters, nameof(command.PatchId));
        if (command.BaseRevision != command.ExpectedRevision)
            throw new FeatureConcurrencyException("The Suggested Change does not target the expected Draft Revision.");
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision, allowReserved: true);
        return new FeatureDraftAuthoringTransition(state, draft);
    }

    public static FeatureDraftAuthoringTransition RecordVerification(FeatureHubState state, RecordFeatureVerification command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Verification);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.Verification.VerifiedAt);
        var verification = ValidateVerification(command.Verification);
        var digest = Fingerprint(command);
        if (Replay(state, command.DraftId, command.IdempotencyId, "verification", digest) is { } replay)
            return replay;
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        return Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                draft.Status,
                draft.Behavior,
                draft.Source,
                verification,
                draft.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                verification.VerifiedAt),
            command.IdempotencyId,
            "verification",
            digest);
    }

    public static FeatureDraftAuthoringTransition MarkInstalled(FeatureHubState state, MarkFeatureDraftInstalled command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.InstalledAt);
        DemandText(command.InstallationId.Value, 256, nameof(command.InstallationId));
        DemandRelease(command.Release);
        var digest = Fingerprint(command);
        if (Replay(state, command.DraftId, command.IdempotencyId, "installed", digest) is { } replay)
            return replay;
        var reservation = ReadInstallationReservation(state, command.DraftId)
            ?? throw new FeatureConcurrencyException(
                "The Feature Draft has no active installation reservation.",
                FeatureCommandRejectionReason.Precondition);
        if (reservation.DraftRevision != command.ExpectedRevision ||
            reservation.InstallationId != command.InstallationId ||
            reservation.Release != command.Release ||
            !string.Equals(reservation.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal))
            throw new FeatureConcurrencyException("The Feature Draft installation reservation does not match this completion.");
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision, allowReserved: true);
        var verification = draft.Verification ?? throw new FeatureConcurrencyException(
            "The Feature Draft has no Verification to install.",
            FeatureCommandRejectionReason.Precondition);
        if (verification.Release != command.Release)
            throw new FeatureConcurrencyException(
                "The installed release must match the exact verified release.",
                FeatureCommandRejectionReason.Precondition);
        if (verification.Failed != 0 || verification.Skipped != 0 || verification.Passed != verification.Total)
            throw new FeatureConcurrencyException(
                "Only a fully successful Verification can be installed.",
                FeatureCommandRejectionReason.Precondition);
        FeaturePublicationTransitions.DemandConfirmedReservation(state, reservation);
        var installed = Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                "installed",
                draft.Behavior,
                draft.Source,
                verification,
                command.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                command.InstalledAt),
            command.IdempotencyId,
            "installed",
            digest);
        return installed with
        {
            State = installed.State with
            {
                DraftInstallationReservations = (installed.State.DraftInstallationReservations ?? [])
                    .Where(candidate => candidate.DraftId != command.DraftId)
                    .ToArray()
            }
        };
    }

    private static FeatureDraft DemandEditableDraft(
        FeatureHubState state,
        FeatureDraftId draftId,
        long expectedRevision,
        bool allowReserved = false)
    {
        var draft = ReadDraft(state, draftId) ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        if (!string.Equals(draft.Status, "draft", StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "An installed Feature Draft is immutable.",
                FeatureCommandRejectionReason.Precondition);
        if (draft.Revision != expectedRevision)
            throw new FeatureConcurrencyException("The Draft Revision changed.");
        if (!allowReserved && (state.DraftInstallationReservations ?? []).Any(candidate => candidate.DraftId == draftId))
            throw new FeatureConcurrencyException(
                "The Feature Draft is reserved for installation.",
                FeatureCommandRejectionReason.Precondition);
        return draft;
    }

    private static FeatureDraftAuthoringTransition Replace(
        FeatureHubState state,
        FeatureDraft current,
        FeatureDraft replacement,
        string idempotencyId,
        string kind,
        string payloadDigest)
    {
        var drafts = (state.Drafts ?? []).ToArray();
        var index = Array.FindIndex(drafts, candidate => candidate.DraftId == current.DraftId);
        if (index < 0)
            throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        drafts[index] = replacement;
        DemandOwnerDraftBudget(drafts);
        var replays = (state.DraftReplays ?? []).ToList();
        while (replays.Count(replay => replay.DraftId == current.DraftId) >= FeatureLimits.DraftReplayRecords)
        {
            var oldest = replays.FindIndex(replay => replay.DraftId == current.DraftId);
            replays.RemoveAt(oldest);
        }
        var replay = new FeatureDraftCommandReplay(
            current.DraftId,
            idempotencyId,
            kind,
            payloadDigest,
            replacement.Status,
            replacement.Behavior,
            replacement.Source,
            replacement.Verification,
            replacement.InstallationId,
            replacement.Revision,
            replacement.UpdatedAt,
            0);
        replay = replay with { Utf8Bytes = ReplayFootprint(replay) };
        replays.Add(replay);
        while (replays.Count > 1 && replays.Sum(candidate => (long)candidate.Utf8Bytes) > FeatureLimits.DraftReplayUtf8Bytes)
            replays.RemoveAt(0);
        return new FeatureDraftAuthoringTransition(
            state with { Drafts = drafts, DraftReplays = replays.ToArray(), Revision = NextRevision(state.Revision) },
            replacement);
    }

    private static long NextRevision(long revision)
    {
        if (revision == long.MaxValue)
            throw new FeatureConcurrencyException("The Feature Draft Revision cannot advance.");
        return revision + 1;
    }

    private static FeatureDraftAuthoringTransition? Replay(
        FeatureHubState state,
        FeatureDraftId draftId,
        string idempotencyId,
        string kind,
        string payloadDigest,
        Func<DateTimeOffset, string>? legacyFingerprint = null)
    {
        DemandDraftId(draftId);
        var replay = (state.DraftReplays ?? []).FirstOrDefault(candidate =>
            candidate.DraftId == draftId && string.Equals(candidate.IdempotencyId, idempotencyId, StringComparison.Ordinal));
        if (replay is null)
            return null;
        var legacyPayloadMatches = legacyFingerprint is not null && string.Equals(
            replay.PayloadDigest,
            legacyFingerprint(replay.ResultUpdatedAt),
            StringComparison.Ordinal);
        if (!string.Equals(replay.Kind, kind, StringComparison.Ordinal) ||
            !string.Equals(replay.PayloadDigest, payloadDigest, StringComparison.Ordinal) && !legacyPayloadMatches)
            throw new FeatureConcurrencyException("The idempotency identifier is already bound to a different authoring command.");
        var current = (state.Drafts ?? []).FirstOrDefault(candidate => candidate.DraftId == draftId)
            ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        return new FeatureDraftAuthoringTransition(state, replay.Result(current));
    }

    private static FeatureVerification ValidateVerification(FeatureVerification verification)
    {
        DemandRelease(verification.Release);
        if (verification.Total <= 0 || verification.Passed < 0 || verification.Failed < 0 || verification.Skipped < 0 ||
            verification.Total != checked(verification.Passed + verification.Failed + verification.Skipped))
            throw new ArgumentException("Verification result counts are invalid.", nameof(verification));
        if (verification.VerifiedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Verification timestamps must be UTC.", nameof(verification));
        return verification;
    }

    internal static FeatureDraftPatch ValidatePatch(FeatureDraftPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        DemandText(patch.PatchId, FeatureLimits.DraftPatchIdCharacters, nameof(patch.PatchId));
        DemandDraftId(patch.DraftId);
        if (patch.BaseRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(patch), "A nonnegative base Draft Revision is required.");
        DemandText(patch.Summary, FeatureLimits.DraftPatchSummaryCharacters, nameof(patch.Summary));
        return patch with
        {
            ReplacementBehavior = ValidateBehavior(patch.ReplacementBehavior),
            ReplacementSource = ValidateSource(patch.ReplacementSource)
        };
    }

    private static void DemandRelease(ReleaseDigest release)
    {
        if (string.IsNullOrEmpty(release.Value) || release.Value.Length != 64 || release.Value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A canonical release digest is required.", nameof(release));
    }

    private static string Fingerprint<T>(T command) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));

    private static FeatureBehavior ValidateBehavior(FeatureBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        ArgumentNullException.ThrowIfNull(behavior.Scenarios);
        if (behavior.Scenarios.Length is 0 or > FeatureLimits.DraftScenarios)
            throw new ArgumentException("Behavior must contain a bounded set of Scenarios.", nameof(behavior));
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        var utf8Bytes = 0;
        foreach (var scenario in behavior.Scenarios)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            DemandText(scenario.ScenarioId, FeatureLimits.DraftScenarioIdCharacters, nameof(scenario.ScenarioId));
            DemandText(scenario.Name, FeatureLimits.DraftScenarioNameCharacters, nameof(scenario.Name));
            DemandText(scenario.Given, FeatureLimits.DraftScenarioStepCharacters, nameof(scenario.Given));
            DemandText(scenario.When, FeatureLimits.DraftScenarioStepCharacters, nameof(scenario.When));
            DemandText(scenario.Then, FeatureLimits.DraftScenarioStepCharacters, nameof(scenario.Then));
            if (!scenarioIds.Add(scenario.ScenarioId))
                throw new ArgumentException("Scenario identifiers must be unique.", nameof(behavior));
            utf8Bytes = checked(utf8Bytes + Encoding.UTF8.GetByteCount(scenario.ScenarioId) + Encoding.UTF8.GetByteCount(scenario.Name) +
                Encoding.UTF8.GetByteCount(scenario.Given) + Encoding.UTF8.GetByteCount(scenario.When) + Encoding.UTF8.GetByteCount(scenario.Then));
            if (utf8Bytes > FeatureLimits.DraftBehaviorUtf8Bytes)
                throw new ArgumentException("Behavior exceeds its UTF-8 bound.", nameof(behavior));
        }
        return behavior;
    }

    private static FeatureSourceSnapshot ValidateSource(FeatureSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var implementationProject = ValidatePath(source.ImplementationProjectPath, nameof(source.ImplementationProjectPath));
        var scenarioProject = ValidatePath(source.ScenarioProjectPath, nameof(source.ScenarioProjectPath));
        if (!implementationProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            !scenarioProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source Snapshot entry paths must be C# projects.", nameof(source));
        ArgumentNullException.ThrowIfNull(source.Files);
        if (source.Files.Length is 0 or > FeatureLimits.DraftSourceFiles)
            throw new ArgumentException("Source Snapshot file count is outside its bound.", nameof(source));
        var collisionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactPaths = new HashSet<string>(StringComparer.Ordinal);
        var utf8Bytes = 0;
        foreach (var file in source.Files)
        {
            ArgumentNullException.ThrowIfNull(file);
            var path = ValidatePath(file.Path, nameof(file.Path));
            if (!collisionPaths.Add(path))
                throw new ArgumentException("Source Snapshot paths must be unique.", nameof(source));
            exactPaths.Add(path);
            ArgumentNullException.ThrowIfNull(file.Content);
            if (file.Content.Contains('\0', StringComparison.Ordinal))
                throw new ArgumentException("Source files cannot contain null characters.", nameof(source));
            var fileBytes = Encoding.UTF8.GetByteCount(file.Content);
            if (fileBytes > FeatureLimits.DraftSourceFileUtf8Bytes)
                throw new ArgumentException("A Source file exceeds its UTF-8 bound.", nameof(source));
            utf8Bytes = checked(utf8Bytes + fileBytes);
            if (utf8Bytes > FeatureLimits.DraftSourceUtf8Bytes)
                throw new ArgumentException("Source Snapshot exceeds its UTF-8 bound.", nameof(source));
        }
        if (!exactPaths.Contains(implementationProject) || !exactPaths.Contains(scenarioProject))
            throw new ArgumentException("Both declared project paths must exist in the Source Snapshot.", nameof(source));
        return source;
    }

    private static string ValidatePath(string path, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(path, parameterName);
        var segments = path.Split('/');
        if (path.Length is 0 or > FeatureLimits.DraftSourcePathCharacters ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.StartsWith('/', StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' ||
            segments.Any(segment => !IsPortablePathSegment(segment)))
            throw new ArgumentException("A bounded canonical relative Source path is required.", parameterName);
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
        var stem = segment.Split('.', 2)[0];
        return !ReservedSourcePathSegments.Contains(stem);
    }

    private static int ReplayFootprint(FeatureDraftCommandReplay replay)
    {
        long bytes = 256;
        bytes += Utf8(replay.DraftId.Value) + Utf8(replay.IdempotencyId) + Utf8(replay.Kind) + Utf8(replay.PayloadDigest) + Utf8(replay.ResultStatus);
        foreach (var scenario in replay.ResultBehavior.Scenarios)
            bytes += Utf8(scenario.ScenarioId) + Utf8(scenario.Name) + Utf8(scenario.Given) + Utf8(scenario.When) + Utf8(scenario.Then);
        bytes += Utf8(replay.ResultSource.ImplementationProjectPath) + Utf8(replay.ResultSource.ScenarioProjectPath);
        foreach (var file in replay.ResultSource.Files)
            bytes += Utf8(file.Path) + Encoding.UTF8.GetByteCount(file.Content);
        bytes += Utf8(replay.ResultVerification?.Release.Value) + Utf8(replay.ResultInstallationId?.Value);
        return checked((int)bytes);
    }

    internal static long OwnerDraftUtf8Bytes(IReadOnlyList<FeatureDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        long bytes = 0;
        foreach (var draft in drafts)
        {
            ArgumentNullException.ThrowIfNull(draft);
            bytes = checked(bytes + DraftFootprint(draft));
        }
        return bytes;
    }

    internal static void DemandOwnerDraftBudget(IReadOnlyList<FeatureDraft> drafts)
    {
        if (OwnerDraftUtf8Bytes(drafts) > FeatureLimits.DraftOwnerUtf8Bytes)
            throw new FeatureLimitExceededException("Feature Drafts exceed the owner-wide live-state byte budget.");
    }

    private static long DraftFootprint(FeatureDraft draft)
    {
        long bytes = 512;
        var request = draft.OriginatingRequest;
        bytes += Utf8(draft.DraftId.Value) + Utf8(request.OperationId) + Utf8(request.ConversationId) + Utf8(request.Text);
        bytes += Utf8(draft.Goal) + Utf8(draft.Status);
        foreach (var scenario in draft.Behavior.Scenarios ?? [])
            bytes += Utf8(scenario.ScenarioId) + Utf8(scenario.Name) + Utf8(scenario.Given) + Utf8(scenario.When) + Utf8(scenario.Then);
        var source = draft.Source;
        bytes += Utf8(source.ImplementationProjectPath) + Utf8(source.ScenarioProjectPath);
        foreach (var file in source.Files ?? [])
            bytes += Utf8(file.Path) + Utf8(file.Content);
        bytes += Utf8(draft.Verification?.Release.Value) + Utf8(draft.InstallationId?.Value);
        return bytes;
    }

    private static int Utf8(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static void DemandDraftId(FeatureDraftId draftId)
    {
        ArgumentNullException.ThrowIfNull(draftId);
        DemandText(draftId.Value, 128, nameof(draftId));
    }

    private static void DemandSubscriptions(string[] subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        if (subscriptions.Length is 0 or > 64 || subscriptions.Any(subscription =>
                string.IsNullOrWhiteSpace(subscription) || subscription.Length > 256 ||
                subscription.Any(char.IsControl) ||
                !string.Equals(subscription, subscription.Trim(), StringComparison.Ordinal)) ||
            subscriptions.Distinct(StringComparer.Ordinal).Count() != subscriptions.Length)
            throw new ArgumentException("Canonical unique Feature subscriptions are required.", nameof(subscriptions));
    }

    private static void DemandMutation(string idempotencyId, DateTimeOffset at)
    {
        DemandText(idempotencyId, 256, nameof(idempotencyId));
        if (at.Offset != TimeSpan.Zero)
            throw new ArgumentException("Feature Draft mutation timestamps must be UTC.", nameof(at));
    }

    private static void DemandText(string value, int maximumCharacters, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumCharacters || value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical value is required.", parameterName);
    }
}
