using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureDraftAuthoringTransitions
{
    private static readonly char[] InvalidSourcePathCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly HashSet<string> ReservedSourcePathSegments = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    public static FeatureDraft? ReadDraft(FeatureHubState state, FeatureDraftId draftId)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandDraftId(draftId);
        return (state.Drafts ?? []).FirstOrDefault(draft => draft.DraftId == draftId);
    }

    public static FeatureDraftAuthoringTransition ReviseBehavior(FeatureHubState state, ReviseFeatureBehavior command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.RevisedAt);
        var behavior = ValidateBehavior(command.Behavior);
        var digest = Fingerprint(command);
        if (Replay(state, command.DraftId, command.IdempotencyId, "behavior", digest) is { } replay)
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
                checked(draft.Revision + 1),
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
        var digest = Fingerprint(command);
        if (Replay(state, command.DraftId, command.IdempotencyId, "source", digest) is { } replay)
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
                checked(draft.Revision + 1),
                draft.CreatedAt,
                command.RevisedAt),
            command.IdempotencyId,
            "source",
            digest);
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
                checked(draft.Revision + 1),
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
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        var verification = draft.Verification ?? throw new FeatureConcurrencyException("The Feature Draft has no Verification to install.");
        if (verification.Release != command.Release)
            throw new FeatureConcurrencyException("The installed release must match the exact verified release.");
        if (verification.Failed != 0 || verification.Skipped != 0 || verification.Passed != verification.Total)
            throw new FeatureConcurrencyException("Only a fully successful Verification can be installed.");
        return Replace(
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
                checked(draft.Revision + 1),
                draft.CreatedAt,
                command.InstalledAt),
            command.IdempotencyId,
            "installed",
            digest);
    }

    private static FeatureDraft DemandEditableDraft(FeatureHubState state, FeatureDraftId draftId, long expectedRevision)
    {
        var draft = ReadDraft(state, draftId) ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        if (!string.Equals(draft.Status, "draft", StringComparison.Ordinal))
            throw new FeatureConcurrencyException("An installed Feature Draft is immutable.");
        if (draft.Revision != expectedRevision)
            throw new FeatureConcurrencyException("The Draft Revision changed.");
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
            state with { Drafts = drafts, DraftReplays = replays.ToArray(), Revision = checked(state.Revision + 1) },
            replacement);
    }

    private static FeatureDraftAuthoringTransition? Replay(
        FeatureHubState state,
        FeatureDraftId draftId,
        string idempotencyId,
        string kind,
        string payloadDigest)
    {
        DemandDraftId(draftId);
        var replay = (state.DraftReplays ?? []).FirstOrDefault(candidate =>
            candidate.DraftId == draftId && string.Equals(candidate.IdempotencyId, idempotencyId, StringComparison.Ordinal));
        if (replay is null)
            return null;
        if (!string.Equals(replay.Kind, kind, StringComparison.Ordinal) ||
            !string.Equals(replay.PayloadDigest, payloadDigest, StringComparison.Ordinal))
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
