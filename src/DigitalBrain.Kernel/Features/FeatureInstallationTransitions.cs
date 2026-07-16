using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;
namespace DigitalBrain.Kernel.Features;

internal static class FeatureInstallationTransitions
{
    public static FeatureAppendTransition AppendExact(
        FeatureInstallationState state,
        ReleaseDigest expectedRelease,
        FeatureInput input,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ActiveRelease != expectedRelease || state.UnconfirmedReleaseSwitch is not null)
            throw new FeatureConcurrencyException(
                "The Feature release is stale or not confirmed.",
                FeatureCommandRejectionReason.Precondition);
        return AppendCore(state, input, now, keepExistingContent: true);
    }

    public static FeatureAppendTransition Append(
        FeatureInstallationState state,
        FeatureInput input,
        DateTimeOffset now) =>
        AppendCore(state, input, now, keepExistingContent: false);

    private static FeatureAppendTransition AppendCore(
        FeatureInstallationState state,
        FeatureInput input,
        DateTimeOffset now,
        bool keepExistingContent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateInput(input);
        var inputDigest = InputDigest(input);
        var completion = state.Completions.FirstOrDefault(entry => Same(entry.InputId, input.InputId));
        if (completion is not null)
        {
            if (!keepExistingContent &&
                !Same(completion.InputDigest, inputDigest) &&
                !(completion.Run is null && Same(completion.InputDigest, LegacyInputDigest(input))))
                throw new FeatureConcurrencyException("The input id is already bound to different content.");
            return new(state, FeatureAppendStatus.Duplicate);
        }
        var pending = state.Inbox.FirstOrDefault(entry => Same(entry.Input.InputId, input.InputId));
        if (pending is not null)
        {
            if (!keepExistingContent &&
                !Same(InputDigest(pending.Input), inputDigest) &&
                !(pending.Input.Origin == FeatureRunOrigin.Unspecified &&
                    Same(LegacyInputDigest(pending.Input), LegacyInputDigest(input))))
                throw new FeatureConcurrencyException("The input id is already bound to different content.");
            if (state.Paused || pending.Parked || pending.AcceptedRelease is null)
                return new(state, FeatureAppendStatus.Paused);
            return new(state, FeatureAppendStatus.Duplicate);
        }
        if (state.Paused)
            return new(state, FeatureAppendStatus.Paused);
        if (state.Inbox.Length >= FeatureLimits.InboxEntries)
        {
            return new(state with { Paused = true, PauseReason = "feature inbox full", Revision = checked(state.Revision + 1) }, FeatureAppendStatus.Full);
        }
        var entry = new FeatureInboxEntry(input, 0, now, false, null, state.ActiveRelease);
        return new(state with { Inbox = [.. state.Inbox, entry], Revision = checked(state.Revision + 1) }, FeatureAppendStatus.Accepted);
    }
    public static FeatureClaimTransition Claim(FeatureInstallationState state, string hostId, DateTimeOffset now, TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > FeatureLimits.RunDeadline)
            throw new FeatureLimitExceededException("A feature lease must be positive and at most 60 seconds.");
        if (state.Paused)
            return new(state, null);
        if (state.Lease is { ExpiresAt: var expiresAt } && expiresAt > now)
            return new(state, null);
        var legacyEntries = state.Inbox.ToArray();
        var parkedLegacy = false;
        for (var legacyIndex = 0; legacyIndex < legacyEntries.Length; legacyIndex++)
        {
            if (legacyEntries[legacyIndex].AcceptedRelease is not null || legacyEntries[legacyIndex].Parked)
                continue;
            legacyEntries[legacyIndex] = legacyEntries[legacyIndex] with
            {
                Parked = true,
                LastFailure = "The input predates release pinning and requires review."
            };
            parkedLegacy = true;
        }
        var current = parkedLegacy
            ? state with { Inbox = legacyEntries, Revision = checked(state.Revision + 1) }
            : state;
        var index = Array.FindIndex(
            current.Inbox,
            entry => !entry.Parked && entry.NotBefore <= now);
        if (index < 0)
        {
            return current.Lease is null
                ? new(current, null)
                : new(current with { Lease = null, Revision = checked(current.Revision + 1) }, null);
        }
        var entries = current.Inbox.ToArray();
        var entry = entries[index];
        if (entry.Attempts >= FeatureLimits.AttemptsPerInput)
        {
            entries[index] = entry with { Parked = true, LastFailure = "The feature host attempt limit was reached." };
            return new(
                current with
                {
                    Inbox = entries,
                    Lease = null,
                    Paused = true,
                    PauseReason = "The feature host attempt limit was reached.",
                    Revision = checked(current.Revision + 1)
                },
                null);
        }
        var attempt = checked(entry.Attempts + 1);
        entries[index] = entry with
        {
            Attempts = attempt,
            LastAttemptAt = now,
            TotalAttempts = checked(VisibleAttempts(entry) + 1)
        };
        var fenceNumber = checked(current.NextFence + 1);
        var fence = new FeatureLeaseFence(entry.Input.InputId, fenceNumber);
        var expires = now.Add(leaseDuration);
        var lease = new FeatureLease(hostId, fence, expires, attempt);
        var next = current with { Inbox = entries, Lease = lease, NextFence = fenceNumber, Revision = checked(current.Revision + 1) };
        return new(next, new FeatureRunClaim(
            entry.Input,
            fence,
            entry.AcceptedRelease!.Value,
            current.StateJson,
            expires,
            attempt));
    }
    public static FeatureInstallationState Fail(FeatureInstallationState state, FeatureLeaseFence fence, DateTimeOffset now, DateTimeOffset retryAt, string safeFailure)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fence);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeFailure);
        if (retryAt < now)
            throw new ArgumentOutOfRangeException(nameof(retryAt));
        var lease = RequireLease(state, fence);
        if (now >= lease.ExpiresAt)
            throw new FeatureConcurrencyException("The feature lease has expired.");
        var index = Array.FindIndex(state.Inbox, entry => Same(entry.Input.InputId, fence.InputId));
        if (index < 0)
            throw new FeatureConcurrencyException("The leased input is no longer pending.");
        var entries = state.Inbox.ToArray();
        var parked = lease.Attempt >= FeatureLimits.AttemptsPerInput;
        entries[index] = entries[index] with { NotBefore = retryAt, Parked = parked, LastFailure = Bound(safeFailure, 256) };
        return state with { Inbox = entries, Lease = null, Paused = state.Paused || parked, Revision = checked(state.Revision + 1) };
    }
    public static FeatureCommitTransition Commit(FeatureInstallationState state, FeatureRunCommit commit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(commit.Fence);
        ArgumentNullException.ThrowIfNull(commit.Intents);
        ArgumentNullException.ThrowIfNull(commit.Usage);
        ValidateJson(commit.NewStateJson, nameof(commit.NewStateJson), FeatureLimits.StateUtf8Bytes);
        ValidateJson(commit.ResultJson, nameof(commit.ResultJson), FeatureLimits.StateUtf8Bytes);
        if (commit.Intents.Count > FeatureLimits.IntentsPerRun)
            throw new FeatureLimitExceededException("A feature run can commit at most 32 intents.");
        if (commit.Usage.Reads < 0 || commit.Usage.Reads > FeatureLimits.ReadsPerRun)
            throw new FeatureLimitExceededException("A feature run can perform at most 20 reads.");
        if (commit.Usage.ModelCalls < 0 || commit.Usage.ModelCalls > FeatureLimits.ModelCallsPerRun)
            throw new FeatureLimitExceededException("A feature run can perform at most four model calls.");
        var commitDigest = CommitDigest(commit);
        var existing = state.Completions.FirstOrDefault(entry => Same(entry.InputId, commit.Fence.InputId));
        if (existing is not null)
        {
            if (existing.Fence != commit.Fence.Fence)
                throw new FeatureConcurrencyException("The feature lease fence is stale.");
            if (!Same(existing.CommitDigest, commitDigest))
                throw new FeatureConcurrencyException("A different result is already committed for this input.");
            return new(state, existing);
        }
        var lease = RequireLease(state, commit.Fence);
        if (now >= lease.ExpiresAt)
            throw new FeatureConcurrencyException("The feature lease has expired.");
        var leasedInput = state.Inbox.FirstOrDefault(entry => Same(entry.Input.InputId, commit.Fence.InputId))
            ?? throw new FeatureConcurrencyException("The leased input is no longer pending.");
        var persistedIntents = commit.Intents.Select(intent =>
        {
            ArgumentNullException.ThrowIfNull(intent);
            ArgumentException.ThrowIfNullOrWhiteSpace(intent.LogicalOperationKey);
            ValidateJson(intent.PayloadJson, nameof(intent.PayloadJson), FeatureLimits.StateUtf8Bytes);
            return new PersistedFeatureIntent(
                FeatureIntentKeys.Create(state.InstallationId, commit.Fence.InputId, intent.LogicalOperationKey),
                intent.Kind,
                intent.PayloadJson,
                null,
                commit.Fence.InputId);
        }).ToArray();
        if (persistedIntents.Select(intent => intent.OperationKey).Distinct(StringComparer.Ordinal).Count() != persistedIntents.Length)
            throw new ArgumentException("Intent logical operation keys must be unique within a run.", nameof(commit));
        var retainedIntents = RetainIntentLedger(state.Intents, persistedIntents);
        var completion = new FeatureCompletion(
            commit.Fence.InputId,
            commit.Fence.Fence,
            commit.ResultJson,
            now,
            commitDigest,
            InputDigest(leasedInput.Input),
            leasedInput.AcceptedRelease,
            FeatureRunProjection.Identity(leasedInput.Input),
            VisibleAttempts(leasedInput),
            leasedInput.LastAttemptAt,
            persistedIntents.Any(intent => intent.Kind == FeatureIntentKind.TextSurface),
            persistedIntents.Count(intent => intent.Kind == FeatureIntentKind.ExternalEffect),
            []);
        var completions = state.Completions.Length == FeatureLimits.InboxEntries
            ? [.. state.Completions.Skip(1), completion]
            : state.Completions.Append(completion).ToArray();
        return new(
            state with
            {
                StateJson = commit.NewStateJson,
                Inbox = state.Inbox.Where(entry => !Same(entry.Input.InputId, commit.Fence.InputId)).ToArray(),
                Lease = null,
                Completions = completions,
                Intents = retainedIntents,
                Revision = checked(state.Revision + 1)
            },
            completion);
    }
    public static FeatureAppendTransition RecordScheduleOccurrence(FeatureInstallationState state, FeatureScheduleOccurrence occurrence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(occurrence);
        ValidateIdentifier(occurrence.ScheduleId, nameof(occurrence.ScheduleId));
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.TraceId);
        ValidateJson(occurrence.PayloadJson, nameof(occurrence.PayloadJson), FeatureLimits.StateUtf8Bytes);
        if (occurrence.ScheduledFor > now)
            throw new ArgumentOutOfRangeException(nameof(occurrence), "A schedule occurrence cannot be recorded before it is due.");
        if (occurrence.NextOccurrenceAt <= now)
            throw new ArgumentOutOfRangeException(nameof(occurrence), "The next schedule occurrence must be in the future.");
        var existingIndex = Array.FindIndex(
            state.Schedules,
            cursor => Same(cursor.ScheduleId, occurrence.ScheduleId));
        if (existingIndex < 0 && state.Schedules.Length >= FeatureLimits.ScheduleCursors)
            throw new FeatureLimitExceededException("Feature schedule cursors exceed the durable ledger capacity.");
        if (existingIndex >= 0)
        {
            var existing = state.Schedules[existingIndex];
            if (occurrence.ScheduledFor < existing.LastOccurrenceAt)
                throw new FeatureConcurrencyException("Schedule occurrences cannot move backward.");
            if (occurrence.ScheduledFor == existing.LastOccurrenceAt && occurrence.NextOccurrenceAt != existing.NextOccurrenceAt)
                throw new FeatureConcurrencyException("A duplicate schedule occurrence has conflicting next-occurrence data.");
        }
        var inputId = ScheduledInputId(state.InstallationId, occurrence.ScheduleId, occurrence.ScheduledFor);
        var appended = Append(
            state,
            new FeatureInput(
                inputId,
                $"schedule.{occurrence.ScheduleId}",
                occurrence.PayloadJson,
                occurrence.ScheduledFor,
                occurrence.CorrelationId,
                occurrence.TraceId,
                Origin: FeatureRunOrigin.Schedule,
                OriginReference: new FeatureRunOriginReference(null, null, occurrence.ScheduleId)),
            now);
        if (appended.Status is not FeatureAppendStatus.Accepted and not FeatureAppendStatus.Duplicate)
            return appended;
        var schedules = appended.State.Schedules.ToArray();
        var cursor = new FeatureScheduleCursor(occurrence.ScheduleId, occurrence.ScheduledFor, occurrence.NextOccurrenceAt);
        if (existingIndex >= 0)
            schedules[existingIndex] = cursor;
        else
            schedules = [.. schedules, cursor];
        var next = appended.State with
        {
            Schedules = schedules,
            Revision = appended.Status == FeatureAppendStatus.Duplicate && existingIndex >= 0
                ? appended.State.Revision
                : checked(appended.State.Revision + 1)
        };
        return new(next, appended.Status);
    }
    public static IReadOnlyList<PersistedFeatureIntent> ListPendingIntents(FeatureInstallationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Intents.Where(intent => intent.Resolution is null && intent.AppliedAt is null && intent.DeclinedAt is null).ToArray();
    }
    public static FeatureInstallationState ApplyIntent(FeatureInstallationState state, string operationKey, DateTimeOffset appliedAt)
        => ResolveLegacyIntent(
            state,
            LegacyResolution(operationKey, InoEffectTerminalKind.Approved, appliedAt, "The action completed."));
    public static FeatureInstallationState DeclineIntent(FeatureInstallationState state, string operationKey, DateTimeOffset declinedAt)
        => ResolveLegacyIntent(
            state,
            LegacyResolution(operationKey, InoEffectTerminalKind.Declined, declinedAt, "The proposed external action was declined."));
    public static FeatureInstallationState ResolveIntent(
        FeatureInstallationState state,
        FeatureEffectResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateResolution(resolution);
        var index = Array.FindIndex(state.Intents, intent => Same(intent.OperationKey, resolution.OperationKey));
        if (index < 0)
        {
            var retained = state.Completions
                .SelectMany(completion => completion.EffectResolutions ?? [])
                .FirstOrDefault(item => Same(item.OperationKey, resolution.OperationKey));
            if (retained is null)
                throw new KeyNotFoundException("The feature intent does not exist.");
            if (retained == resolution)
                return state;
            throw new FeatureConcurrencyException("The Feature Run already has a different effect resolution.");
        }
        var current = state.Intents[index];
        if (current.Resolution is { } stored)
        {
            if (stored == resolution)
                return state;
            throw new FeatureConcurrencyException("The feature intent already has a different terminal resolution.");
        }
        var legacyKind = LegacyTerminalKind(current);
        if (legacyKind is not null && legacyKind != resolution.TerminalKind)
            throw new FeatureConcurrencyException("The feature intent already has a different terminal resolution.");
        var digest = current.PayloadDigest ?? PayloadDigest(current.PayloadJson);
        var intents = state.Intents.ToArray();
        intents[index] = current with
        {
            PayloadJson = string.Empty,
            AppliedAt = resolution.TerminalKind == InoEffectTerminalKind.Approved
                ? resolution.ResolvedAt
                : current.AppliedAt,
            DeclinedAt = resolution.TerminalKind == InoEffectTerminalKind.Declined
                ? resolution.ResolvedAt
                : current.DeclinedAt,
            PayloadDigest = digest,
            Resolution = resolution
        };
        var completions = state.Completions;
        if (current.Kind == FeatureIntentKind.ExternalEffect && current.InputId is not null)
        {
            var completionIndex = Array.FindIndex(
                completions,
                completion => Same(completion.InputId, current.InputId));
            if (completionIndex >= 0)
            {
                var existing = completions[completionIndex].EffectResolutions ?? [];
                var existingResolution = existing.FirstOrDefault(item => Same(item.OperationKey, resolution.OperationKey));
                if (existingResolution is not null && existingResolution != resolution)
                    throw new FeatureConcurrencyException("The Feature Run already has a different effect resolution.");
                var effectResolutions = existingResolution is null
                    ? existing.Append(resolution)
                        .OrderBy(item => item.OperationKey, StringComparer.Ordinal)
                        .ToArray()
                    : existing;
                if (effectResolutions.Length > FeatureLimits.IntentsPerRun)
                    throw new FeatureLimitExceededException("A Feature Run can retain at most 32 effect resolutions.");
                completions = completions.ToArray();
                completions[completionIndex] = completions[completionIndex] with
                {
                    EffectResolutions = effectResolutions
                };
            }
        }
        return state with
        {
            Intents = intents,
            Completions = completions,
            Revision = checked(state.Revision + 1)
        };
    }
    public static FeatureInstallationState Pause(FeatureInstallationState state, string reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var bounded = Bound(reason, 256);
        if (state.Paused && Same(state.PauseReason ?? string.Empty, bounded))
            return state;
        return state with { Paused = true, PauseReason = bounded, Revision = checked(state.Revision + 1) };
    }
    public static FeatureInstallationState Resume(FeatureInstallationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Paused)
            return state;
        var inbox = state.Inbox.Select(entry => entry.Parked
            ? entry with
            {
                Parked = false,
                Attempts = 0,
                LastFailure = null,
                TotalAttempts = VisibleAttempts(entry)
            }
            : entry).ToArray();
        return state with { Paused = false, PauseReason = null, Inbox = inbox, Revision = checked(state.Revision + 1) };
    }
    public static FeatureInstallationState SwitchRelease(FeatureInstallationState state, ReleaseDigest release)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ActiveRelease == release)
            return state;
        return state with { ActiveRelease = release, PreviousRelease = state.ActiveRelease, Revision = checked(state.Revision + 1) };
    }
    public static FeatureInstallationState Rollback(FeatureInstallationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.PreviousRelease is not { } previous)
            return state;
        return state with { ActiveRelease = previous, PreviousRelease = null, Revision = checked(state.Revision + 1) };
    }
    private static FeatureLease RequireLease(FeatureInstallationState state, FeatureLeaseFence fence)
    {
        if (state.Lease is not { } lease || !Same(lease.Fence.InputId, fence.InputId) || lease.Fence.Fence != fence.Fence)
            throw new FeatureConcurrencyException("The feature lease fence is stale.");
        return lease;
    }

    private static int VisibleAttempts(FeatureInboxEntry entry) =>
        Math.Max(entry.Attempts, entry.TotalAttempts);
    internal static void ValidateInput(FeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateIdentifier(input.InputId, nameof(input.InputId));
        ValidateIdentifier(input.Kind, nameof(input.Kind));
        ValidateIdentifier(input.CorrelationId, nameof(input.CorrelationId));
        ValidateIdentifier(input.TraceId, nameof(input.TraceId));
        if (input.CausationId is not null)
            ValidateIdentifier(input.CausationId, nameof(input.CausationId));
        if (input.OccurredAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Feature input timestamps must be UTC.", nameof(input));
        FeatureRunProjection.ValidateOrigin(input);
        ValidateJson(input.PayloadJson, nameof(input.PayloadJson), FeatureLimits.StateUtf8Bytes);
    }
    internal static string InputDigest(FeatureInput input)
    {
        var canonical = InputDigestCanonical(input);
        FeatureRunProjection.AppendOriginDigest(canonical, input);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string LegacyInputDigest(FeatureInput input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(InputDigestCanonical(input).ToString())));

    private static StringBuilder InputDigestCanonical(FeatureInput input)
    {
        ValidateInput(input);
        var canonical = new StringBuilder();
        AppendCanonical(canonical, input.InputId);
        AppendCanonical(canonical, input.Kind);
        AppendCanonical(canonical, input.PayloadJson);
        canonical.Append(input.OccurredAt.UtcTicks).Append(';');
        AppendCanonical(canonical, input.CorrelationId);
        AppendCanonical(canonical, input.TraceId);
        AppendCanonical(canonical, input.CausationId ?? string.Empty);
        return canonical;
    }
    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(char.IsControl) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical identifier is required.", parameterName);
    }
    private static void ValidateJson(string json, string parameterName, int maximumUtf8Bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json, parameterName);
        if (Encoding.UTF8.GetByteCount(json) > maximumUtf8Bytes)
            throw new FeatureLimitExceededException($"{parameterName} exceeds {maximumUtf8Bytes} UTF-8 bytes.");
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Canonical JSON is required.", parameterName, exception);
        }
    }
    private static string ScheduledInputId(FeatureInstallationId installationId, string scheduleId, DateTimeOffset scheduledFor)
    {
        var canonical = $"{installationId.Value.Length}:{installationId.Value}{scheduleId.Length}:{scheduleId}{scheduledFor.UtcTicks}";
        return "schedule-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private static string CommitDigest(FeatureRunCommit commit)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, commit.NewStateJson);
        AppendCanonical(canonical, commit.ResultJson);
        canonical.Append(commit.Usage.Reads).Append(':').Append(commit.Usage.ModelCalls).Append(';');
        foreach (var intent in commit.Intents)
        {
            ArgumentNullException.ThrowIfNull(intent);
            AppendCanonical(canonical, intent.LogicalOperationKey);
            canonical.Append((int)intent.Kind).Append(';');
            AppendCanonical(canonical, intent.PayloadJson);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
    private static PersistedFeatureIntent[] RetainIntentLedger(IReadOnlyList<PersistedFeatureIntent> existing, IReadOnlyList<PersistedFeatureIntent> appended)
    {
        var pending = existing.Where(intent => ResolvedAt(intent) is null).ToArray();
        var requiredCount = checked(pending.Length + appended.Count);
        var requiredBytes = pending.Sum(IntentBytes) + appended.Sum(IntentBytes);
        if (requiredCount > FeatureLimits.IntentLedgerEntries || requiredBytes > FeatureLimits.IntentLedgerUtf8Bytes)
            throw new FeatureLimitExceededException("Pending feature intents exceed the durable intent ledger capacity.");
        var remainingCount = FeatureLimits.IntentLedgerEntries - requiredCount;
        var remainingBytes = FeatureLimits.IntentLedgerUtf8Bytes - requiredBytes;
        var applied = new List<PersistedFeatureIntent>();
        foreach (var intent in existing.Where(intent => ResolvedAt(intent) is not null)
            .OrderByDescending(ResolvedAt)
            .ThenByDescending(intent => intent.OperationKey, StringComparer.Ordinal))
        {
            var bytes = IntentBytes(intent);
            if (applied.Count >= remainingCount || bytes > remainingBytes)
                continue;
            applied.Add(intent);
            remainingBytes -= bytes;
        }
        applied.Reverse();
        return [.. applied, .. pending, .. appended];
    }
    private static DateTimeOffset? ResolvedAt(PersistedFeatureIntent intent) =>
        intent.Resolution?.ResolvedAt ?? intent.AppliedAt ?? intent.DeclinedAt;
    private static int IntentBytes(PersistedFeatureIntent intent) =>
        Encoding.UTF8.GetByteCount(intent.OperationKey) +
        Encoding.UTF8.GetByteCount(intent.PayloadJson) +
        Encoding.UTF8.GetByteCount(intent.PayloadDigest ?? string.Empty) +
        Encoding.UTF8.GetByteCount(intent.Resolution?.DecisionId ?? string.Empty) +
        Encoding.UTF8.GetByteCount(intent.Resolution?.ActorScope ?? string.Empty) +
        Encoding.UTF8.GetByteCount(intent.Resolution?.SafeResult ?? string.Empty);
    private static FeatureEffectResolution LegacyResolution(
        string operationKey,
        InoEffectTerminalKind terminalKind,
        DateTimeOffset resolvedAt,
        string safeResult) => new(
            operationKey,
            "legacy-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(operationKey))),
            new string('0', 64),
            terminalKind,
            resolvedAt,
            safeResult);
    private static FeatureInstallationState ResolveLegacyIntent(
        FeatureInstallationState state,
        FeatureEffectResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(state);
        var intent = state.Intents.FirstOrDefault(item => Same(item.OperationKey, resolution.OperationKey));
        if (intent?.Resolution is { } stored && stored.TerminalKind == resolution.TerminalKind)
            return state;
        return ResolveIntent(state, resolution);
    }
    private static InoEffectTerminalKind? LegacyTerminalKind(PersistedFeatureIntent intent) =>
        intent.AppliedAt is not null
            ? InoEffectTerminalKind.Approved
            : intent.DeclinedAt is not null
                ? InoEffectTerminalKind.Declined
                : null;
    private static void ValidateResolution(FeatureEffectResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution.OperationKey);
        DemandBoundedText(resolution.DecisionId, 256, nameof(resolution.DecisionId));
        if (!RuntimeStateKeys.IsScopeHash(resolution.ActorScope))
            throw new ArgumentException("A canonical actor scope digest is required.", nameof(resolution.ActorScope));
        if (!Enum.IsDefined(resolution.TerminalKind) || resolution.TerminalKind == InoEffectTerminalKind.None)
            throw new ArgumentOutOfRangeException(nameof(resolution.TerminalKind));
        if (resolution.ResolvedAt == default || resolution.ResolvedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("An effect resolution timestamp must be UTC.", nameof(resolution.ResolvedAt));
        DemandBoundedText(resolution.SafeResult, 512, nameof(resolution.SafeResult));
    }
    private static void DemandBoundedText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException("Bounded safe text is required.", parameterName);
    }
    private static string PayloadDigest(string payloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
    private static void AppendCanonical(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value).Append(';');
    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
}
