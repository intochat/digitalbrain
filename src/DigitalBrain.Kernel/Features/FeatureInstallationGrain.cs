using System.Diagnostics;
using DigitalBrain.Kernel.Contracts;
using Orleans.Runtime;
namespace DigitalBrain.Kernel.Features;

[GrainType("digitalbrain.v3.feature-installation")]
internal sealed class FeatureInstallationGrain(
    [PersistentState("feature-installation")] IPersistentState<FeatureInstallationState> persistentState,
    [PersistentState("feature-installation-reservation-hold")] IPersistentState<FeatureRuntimeReservationHold> reservationState,
    TimeProvider timeProvider) : Grain, IFeatureInstallationGrain
{
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Features.Installation");
    public async Task InitializeAsync(ReleaseDigest release)
    {
        using var activity = Start("initialize");
        DemandNoReservation();
        var (_, installationId) = ParseKey();
        if (persistentState.RecordExists)
        {
            if (RequiredState().ActiveRelease != release)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            return;
        }
        await WriteAsync(FeatureInstallationState.Create(release, installationId));
    }
    public async Task<FeatureAppendStatus> AppendAsync(FeatureInput input)
    {
        using var activity = Start("append", input);
        if (HasReservation)
        {
            FeatureInstallationTransitions.ValidateInput(input);
            return FeatureAppendStatus.Paused;
        }
        var current = RequiredState();
        var transition = Domain(() => FeatureInstallationTransitions.Append(current, input, timeProvider.GetUtcNow()));
        if (current.UnconfirmedReleaseSwitch is not null && transition.Status == FeatureAppendStatus.Full)
            return FeatureAppendStatus.Full;
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return transition.Status;
    }
    public async Task<FeatureAppendStatus> AppendExactAsync(ReleaseDigest expectedRelease, FeatureInput input)
    {
        using var activity = Start("append-exact", input);
        if (HasReservation)
        {
            FeatureInstallationTransitions.ValidateInput(input);
            return FeatureAppendStatus.Paused;
        }
        var current = RequiredState();
        var transition = Domain(() => FeatureInstallationTransitions.AppendExact(
            current,
            expectedRelease,
            input,
            timeProvider.GetUtcNow()));
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return transition.Status;
    }
    public async Task<FeatureRunClaim?> ClaimAsync(string hostId, TimeSpan leaseDuration)
    {
        using var activity = Start("claim");
        if (HasReservation)
            return null;
        if (RequiredState().UnconfirmedReleaseSwitch is not null)
            return null;
        var transition = Domain(() => FeatureInstallationTransitions.Claim(RequiredState(), hostId, timeProvider.GetUtcNow(), leaseDuration));
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return transition.Claim;
    }
    public async Task<FeatureFailureDisposition> FailAsync(FeatureLeaseFence fence, DateTimeOffset retryAt, string safeFailure)
    {
        using var activity = Start("fail");
        var next = Domain(() => FeatureInstallationTransitions.Fail(RequiredState(), fence, timeProvider.GetUtcNow(), retryAt, safeFailure));
        await WriteAsync(next);
        return next.Inbox.Single(entry => string.Equals(entry.Input.InputId, fence.InputId, StringComparison.Ordinal)).Parked
            ? FeatureFailureDisposition.Parked
            : FeatureFailureDisposition.RetryScheduled;
    }
    public async Task<FeatureAppendStatus> RecordScheduleOccurrenceAsync(FeatureScheduleOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        using var activity = Start("schedule", correlationId: occurrence.CorrelationId, traceId: occurrence.TraceId);
        if (HasReservation)
            return FeatureAppendStatus.Paused;
        var current = RequiredState();
        var transition = Domain(() => FeatureInstallationTransitions.RecordScheduleOccurrence(current, occurrence, timeProvider.GetUtcNow()));
        if (current.UnconfirmedReleaseSwitch is not null && transition.Status == FeatureAppendStatus.Full)
            return FeatureAppendStatus.Full;
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return transition.Status;
    }
    public async Task<FeatureCompletionReceipt> CommitAsync(FeatureRunCommit commit)
    {
        using var activity = Start("commit");
        var transition = Domain(() => FeatureInstallationTransitions.Commit(RequiredState(), commit, timeProvider.GetUtcNow()));
        if (!ReferenceEquals(transition.State, persistentState.State))
        {
            await WriteAsync(transition.State);
        }
        return Receipt(transition.Completion);
    }
    public Task<FeatureIntentStatus[]> ListPendingIntentsAsync()
    {
        using var activity = Start("list-intents");
        return Task.FromResult(Domain(() => FeatureInstallationTransitions.ListPendingIntents(RequiredState()))
            .Select(IntentStatus)
            .ToArray());
    }
    public async Task ApplyIntentAsync(string operationKey)
    {
        using var activity = Start("apply-intent");
        DemandNoReservation();
        var next = Domain(() => FeatureInstallationTransitions.ApplyIntent(RequiredState(), operationKey, timeProvider.GetUtcNow()));
        if (ReferenceEquals(next, persistentState.State))
            return;
        await WriteAsync(next);
    }
    public async Task DeclineIntentAsync(string operationKey)
    {
        using var activity = Start("decline-intent");
        DemandNoReservation();
        var next = Domain(() => FeatureInstallationTransitions.DeclineIntent(RequiredState(), operationKey, timeProvider.GetUtcNow()));
        if (ReferenceEquals(next, persistentState.State))
            return;
        await WriteAsync(next);
    }
    public Task PauseAsync(string reason) => PersistAsync("pause", state => !HasReservation && state.UnconfirmedReleaseSwitch is null
        ? FeatureInstallationTransitions.Pause(state, reason)
        : throw new FeatureConcurrencyException("The Feature runtime is reserved or has an unconfirmed release switch."));
    public Task ResumeAsync() => PersistAsync("resume", state => !HasReservation && state.UnconfirmedReleaseSwitch is null
        ? FeatureInstallationTransitions.Resume(state)
        : throw new FeatureConcurrencyException("The Feature runtime is reserved or has an unconfirmed release switch."));
    public Task SwitchReleaseAsync(ReleaseDigest release) =>
        PersistAsync("switch-release", state => !HasReservation && state.UnconfirmedReleaseSwitch is null
            ? FeatureInstallationTransitions.SwitchRelease(state, release)
            : throw new FeatureConcurrencyException("The Feature runtime is reserved or has an unconfirmed release switch."));
    public async Task<FeatureRuntimeReservationSnapshot> EstablishReservationAsync(FeatureRuntimeReservation reservation)
    {
        using var activity = Start("establish-reservation");
        DemandReservationCoordinates(reservation);
        if (HasReservation)
        {
            DemandExactReservation(reservation);
            return ReservationSnapshot();
        }
        if (reservation.RuntimeRevision is null)
        {
            if (persistentState.RecordExists || reservation.RuntimeActiveRelease is not null ||
                reservation.RuntimePreviousRelease is not null || reservation.RuntimePaused is not null ||
                reservation.RuntimePauseReason is not null)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        }
        else
        {
            var state = RequiredState();
            if (state.Revision < reservation.RuntimeRevision || state.ActiveRelease != reservation.RuntimeActiveRelease ||
                state.PreviousRelease != reservation.RuntimePreviousRelease || state.UnconfirmedReleaseSwitch is not null)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            var exactPause = state.Paused == reservation.RuntimePaused &&
                string.Equals(state.PauseReason, reservation.RuntimePauseReason, StringComparison.Ordinal);
            var recoverableBackpressure = reservation.RuntimePaused == false && state.Paused &&
                string.Equals(state.PauseReason, "feature inbox full", StringComparison.Ordinal);
            if (!exactPause && !recoverableBackpressure)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        }
        await WriteReservationAsync(new FeatureRuntimeReservationHold(
            reservation,
            FeatureRuntimeReservationPhase.Reserved));
        return ReservationSnapshot();
    }
    public Task<FeatureRuntimeReservationSnapshot?> ReadReservationAsync()
    {
        using var activity = Start("read-reservation");
        return Task.FromResult(HasReservation ? ReservationSnapshot() : null);
    }
    public async Task ActivateReservedReleaseAsync(FeatureRuntimeReservation reservation)
    {
        using var activity = Start("activate-reserved-release");
        DemandReservationCoordinates(reservation);
        var hold = DemandExactReservation(reservation);
        if (hold.Phase == FeatureRuntimeReservationPhase.Switched)
        {
            DemandActivatedReservation(reservation);
            return;
        }
        if (hold.Phase != FeatureRuntimeReservationPhase.Reserved)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        if (reservation.RuntimeRevision is null)
        {
            if (!persistentState.RecordExists)
                await WriteAsync(FeatureInstallationState.Create(reservation.CandidateRelease, reservation.InstallationId));
            else
                DemandPristineReservedInitialization(RequiredState(), reservation);
        }
        else
        {
            var state = RequiredState();
            if (IsExactReservationSwitch(state, reservation))
            {
                await WriteReservationAsync(hold with { Phase = FeatureRuntimeReservationPhase.Switched });
                return;
            }
            if (state.ActiveRelease != reservation.RuntimeActiveRelease ||
                state.PreviousRelease != reservation.RuntimePreviousRelease ||
                state.Revision < reservation.RuntimeRevision || state.UnconfirmedReleaseSwitch is not null)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            var now = timeProvider.GetUtcNow();
            if (state.Lease is { ExpiresAt: var expiresAt } && expiresAt > now)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            var normalized = state.Lease is null
                ? state
                : state with { Lease = null, Revision = checked(state.Revision + 1) };
            normalized = NormalizeReservedBackpressure(normalized, reservation);
            if (normalized.Paused != reservation.RuntimePaused ||
                !string.Equals(normalized.PauseReason, reservation.RuntimePauseReason, StringComparison.Ordinal))
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            FeatureInstallationState switched;
            if (normalized.ActiveRelease == reservation.CandidateRelease)
            {
                switched = normalized with
                {
                    Revision = checked(normalized.Revision + 1),
                    UnconfirmedReleaseSwitch = new FeatureReleaseSwitch(
                        reservation.ReservationToken,
                        normalized.ActiveRelease,
                        normalized.PreviousRelease,
                        reservation.CandidateRelease,
                        normalized.Revision,
                        checked(normalized.Revision + 1))
                };
            }
            else
            {
                var changed = FeatureInstallationTransitions.SwitchRelease(normalized, reservation.CandidateRelease);
                switched = changed with
                {
                    UnconfirmedReleaseSwitch = new FeatureReleaseSwitch(
                        reservation.ReservationToken,
                        normalized.ActiveRelease,
                        normalized.PreviousRelease,
                        reservation.CandidateRelease,
                        normalized.Revision,
                        changed.Revision)
                };
            }
            await WriteAsync(switched);
        }
        await WriteReservationAsync(hold with { Phase = FeatureRuntimeReservationPhase.Switched });
    }
    public async Task ResetReservedReleaseAsync(FeatureRuntimeReservation reservation, bool requireRuntimeAbsence)
    {
        using var activity = Start("reset-reserved-release");
        DemandReservationCoordinates(reservation);
        if (!HasReservation)
        {
            var baseline = DemandRecoverableMissingResetReservation(reservation, requireRuntimeAbsence);
            await WriteReservationAsync(new FeatureRuntimeReservationHold(
                reservation,
                FeatureRuntimeReservationPhase.Resetting));
            if (baseline is not null)
            {
                var normalized = NormalizeReservedBackpressure(baseline, reservation);
                if (!ReferenceEquals(normalized, baseline))
                    await WriteAsync(normalized);
            }
            return;
        }
        var hold = DemandExactReservation(reservation);
        if (hold.Phase is not FeatureRuntimeReservationPhase.Reserved and
            not FeatureRuntimeReservationPhase.Switched and
            not FeatureRuntimeReservationPhase.Resetting)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var resetAlreadyStarted = hold.Phase == FeatureRuntimeReservationPhase.Resetting;
        if (hold.Phase != FeatureRuntimeReservationPhase.Resetting)
        {
            hold = hold with { Phase = FeatureRuntimeReservationPhase.Resetting };
            await WriteReservationAsync(hold);
        }
        if (reservation.RuntimeRevision is null)
        {
            if (!persistentState.RecordExists)
                return;
            if (requireRuntimeAbsence)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            DemandPristineReservedInitialization(RequiredState(), reservation);
            await PersistedStateReconciliation.ClearWithReconciliationAsync(persistentState);
            return;
        }
        if (requireRuntimeAbsence)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var state = RequiredState();
        if (resetAlreadyStarted && state.ActiveRelease == reservation.RuntimeActiveRelease &&
            state.PreviousRelease == reservation.RuntimePreviousRelease &&
            state.Revision >= reservation.RuntimeRevision && state.UnconfirmedReleaseSwitch is null)
        {
            var normalizedBaseline = NormalizeReservedBackpressure(state, reservation);
            if (normalizedBaseline.Paused != reservation.RuntimePaused ||
                !string.Equals(normalizedBaseline.PauseReason, reservation.RuntimePauseReason, StringComparison.Ordinal))
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            if (!ReferenceEquals(normalizedBaseline, state))
                await WriteAsync(normalizedBaseline);
            return;
        }
        var now = timeProvider.GetUtcNow();
        if (state.Lease is { ExpiresAt: var expiresAt } && expiresAt > now)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var restored = state.Lease is null
            ? state
            : state with { Lease = null, Revision = checked(state.Revision + 1) };
        if (restored.UnconfirmedReleaseSwitch is { } releaseSwitch)
        {
            if (!string.Equals(releaseSwitch.OperationToken, reservation.ReservationToken, StringComparison.Ordinal) ||
                releaseSwitch.FromActiveRelease != reservation.RuntimeActiveRelease ||
                releaseSwitch.FromPreviousRelease != reservation.RuntimePreviousRelease ||
                releaseSwitch.ToRelease != reservation.CandidateRelease ||
                restored.ActiveRelease != reservation.CandidateRelease ||
                restored.Revision != releaseSwitch.SwitchRevision)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            restored = restored with
            {
                ActiveRelease = reservation.RuntimeActiveRelease.Value,
                PreviousRelease = reservation.RuntimePreviousRelease,
                Revision = checked(restored.Revision + 1),
                UnconfirmedReleaseSwitch = null
            };
        }
        else if (restored.ActiveRelease != reservation.RuntimeActiveRelease ||
                 restored.PreviousRelease != reservation.RuntimePreviousRelease ||
                 restored.Revision < reservation.RuntimeRevision)
        {
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        }
        restored = NormalizeReservedBackpressure(restored, reservation);
        if (restored.Paused != reservation.RuntimePaused ||
            !string.Equals(restored.PauseReason, reservation.RuntimePauseReason, StringComparison.Ordinal))
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        if (!FeatureStateEquality.Same(restored, state))
            await WriteAsync(restored);
    }
    public async Task ReleaseReservationAsync(FeatureRuntimeReservationRelease release)
    {
        using var activity = Start("release-reservation");
        ArgumentNullException.ThrowIfNull(release);
        DemandReservationCoordinates(release.Reservation);
        if (!HasReservation)
            return;
        var hold = DemandExactReservation(release.Reservation);
        if (hold.Phase != release.ExpectedPhase)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        if (release.RequireRuntimeAbsent)
        {
            if (persistentState.RecordExists || release.ExpectedActiveRelease is not null ||
                release.ExpectedPreviousRelease is not null)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        }
        else
        {
            var state = RequiredState();
            if (state.ActiveRelease != release.ExpectedActiveRelease ||
                state.PreviousRelease != release.ExpectedPreviousRelease)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            if (release.ExpectedPhase == FeatureRuntimeReservationPhase.Switched &&
                state.UnconfirmedReleaseSwitch is { } pending)
            {
                if (!string.Equals(pending.OperationToken, release.Reservation.ReservationToken, StringComparison.Ordinal) ||
                    pending.ToRelease != release.ExpectedActiveRelease)
                    throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
                await WriteAsync(state with { UnconfirmedReleaseSwitch = null });
            }
            else if (state.UnconfirmedReleaseSwitch is not null)
            {
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            }
        }
        await PersistedStateReconciliation.ClearWithReconciliationAsync(reservationState);
    }
    public async Task BeginReleaseSwitchAsync(ReleaseDigest release, string operationToken)
    {
        using var activity = Start("begin-release-switch");
        DemandNoReservation();
        if (string.IsNullOrWhiteSpace(operationToken) || operationToken.Length > 256 || operationToken.Any(char.IsControl))
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var state = RequiredState();
        if (state.UnconfirmedReleaseSwitch is { } existing)
        {
            if (string.Equals(existing.OperationToken, operationToken, StringComparison.Ordinal) &&
                existing.ToRelease == release && state.ActiveRelease == release &&
                state.PreviousRelease == existing.FromActiveRelease && state.Revision >= existing.SwitchRevision)
                return;
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        }
        if (state.ActiveRelease == release)
        {
            var normalizedSameRelease = NormalizeBackpressurePause(state);
            if (!ReferenceEquals(normalizedSameRelease, state))
                await WriteAsync(normalizedSameRelease);
            return;
        }
        var now = timeProvider.GetUtcNow();
        if (state.Lease is { ExpiresAt: var expiresAt } && expiresAt > now)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var normalized = state.Lease is not null
            ? state with { Lease = null, Revision = checked(state.Revision + 1) }
            : state;
        normalized = NormalizeBackpressurePause(normalized);
        var switched = FeatureInstallationTransitions.SwitchRelease(normalized, release);
        await WriteAsync(switched with
        {
            UnconfirmedReleaseSwitch = new FeatureReleaseSwitch(
                operationToken,
                normalized.ActiveRelease,
                normalized.PreviousRelease,
                release,
                normalized.Revision,
                switched.Revision)
        });
    }
    public async Task ConfirmReleaseSwitchAsync(ReleaseDigest release)
    {
        using var activity = Start("confirm-release-switch");
        DemandNoReservation();
        var state = RequiredState();
        if (state.UnconfirmedReleaseSwitch is not { } pending)
            return;
        if (pending.ToRelease != release || state.ActiveRelease != release)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        await WriteAsync(state with { UnconfirmedReleaseSwitch = null });
    }
    public Task ClearBackpressurePauseAsync()
    {
        DemandNoReservation();
        return PersistAsync("clear-backpressure-pause", NormalizeBackpressurePause);
    }
    public async Task DiscardUnpublishedAsync(ReleaseDigest release, bool requireAbsent)
    {
        using var activity = Start("discard-unpublished");
        DemandNoReservation();
        if (!persistentState.RecordExists)
            return;
        if (requireAbsent)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var state = RequiredState();
        var (_, installationId) = ParseKey();
        if (state.InstallationId != installationId || state.ActiveRelease != release || state.PreviousRelease is not null || state.StateJson != "{}" ||
            state.Paused || state.Inbox.Length != 0 || state.Lease is not null || state.Completions.Length != 0 ||
            state.Intents.Length != 0 || state.NextFence != 0 || state.Revision != 0 || state.PauseReason is not null ||
            state.Schedules.Length != 0 || state.UnconfirmedReleaseSwitch is not null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        await PersistedStateReconciliation.ClearWithReconciliationAsync(persistentState);
    }
    public async Task RestoreUnpublishedCandidateAsync(
        ReleaseDigest candidateRelease,
        ReleaseDigest expectedActiveRelease,
        ReleaseDigest? expectedPreviousRelease,
        long minimumFromRevision)
    {
        using var activity = Start("restore-unpublished-candidate");
        DemandNoReservation();
        if (minimumFromRevision < 0)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var state = RequiredState();
        if (state.UnconfirmedReleaseSwitch is not { } pending ||
            pending.ToRelease != candidateRelease || pending.FromActiveRelease != expectedActiveRelease ||
            pending.FromPreviousRelease != expectedPreviousRelease || pending.FromRevision < minimumFromRevision ||
            state.ActiveRelease != candidateRelease || state.PreviousRelease != expectedActiveRelease ||
            state.Revision != pending.SwitchRevision)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        await WriteAsync(state with
        {
            ActiveRelease = expectedActiveRelease,
            PreviousRelease = expectedPreviousRelease,
            Revision = checked(state.Revision + 1),
            UnconfirmedReleaseSwitch = null
        });
    }
    public Task RollbackAsync() => PersistAsync("rollback", state => !HasReservation && state.UnconfirmedReleaseSwitch is null
        ? FeatureInstallationTransitions.Rollback(state)
        : throw new FeatureConcurrencyException("A reserved or unconfirmed Feature release switch cannot be rolled back directly."));
    public Task<FeatureInstallationSnapshot> ReadAsync()
    {
        using var activity = Start("read");
        var state = RequiredState();
        return Task.FromResult(new FeatureInstallationSnapshot(
            state.InstallationId,
            state.ActiveRelease,
            state.PreviousRelease,
            state.StateJson,
            state.Paused,
            state.PauseReason,
            state.Inbox.Select(entry => entry.Input).ToArray(),
            state.Lease is { } lease ? new FeatureLeaseStatus(lease.HostId, lease.Fence, lease.ExpiresAt, lease.Attempt) : null,
            state.Completions.Select(Receipt).ToArray(),
            state.Intents.Select(IntentStatus).ToArray(),
            state.Schedules.Select(schedule => new FeatureScheduleStatus(schedule.ScheduleId, schedule.LastOccurrenceAt, schedule.NextOccurrenceAt)).ToArray(),
            state.Revision,
            state.Inbox.Where(entry => entry.Parked).Select(entry => new FeatureParkedInput(entry.Input, entry.Attempts, entry.LastFailure)).ToArray(),
            state.UnconfirmedReleaseSwitch is null
                ? null
                : new FeatureReleaseSwitchSnapshot(
                    state.UnconfirmedReleaseSwitch.OperationToken,
                    state.UnconfirmedReleaseSwitch.FromActiveRelease,
                    state.UnconfirmedReleaseSwitch.FromPreviousRelease,
                    state.UnconfirmedReleaseSwitch.ToRelease,
                    state.UnconfirmedReleaseSwitch.FromRevision,
                    state.UnconfirmedReleaseSwitch.SwitchRevision),
            FeatureRunProjection.Project(state)));
    }
    public Task<FeatureRunCollectionSnapshot> ReadRunsAsync(FeatureRunReadRequest request)
    {
        using var activity = Start("read-runs");
        DemandRunReadRequest(request);
        var state = RequiredState();
        var runs = FeatureRunProjection.Project(state)
            .Where(candidate => request.Status is null || candidate.Status == request.Status)
            .Where(candidate => request.Origin is null || candidate.Origin == request.Origin)
            .Where(candidate => request.RunId is null || string.Equals(candidate.RunId, request.RunId, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.CompletedAt ?? candidate.OccurredAt)
            .ThenByDescending(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.RunId, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToArray();
        return Task.FromResult(new FeatureRunCollectionSnapshot(
            state.InstallationId,
            state.ActiveRelease,
            state.Revision,
            runs));
    }
    private static void DemandRunReadRequest(FeatureRunReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > FeatureRunReadRequest.MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Origin is { } origin && (!Enum.IsDefined(origin) || origin == FeatureRunOrigin.Unspecified))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.RunId is { } runId &&
            (string.IsNullOrWhiteSpace(runId) || runId.Length > 256 || runId.Any(char.IsControl) ||
             !string.Equals(runId, runId.Trim(), StringComparison.Ordinal)))
            throw new ArgumentException("A bounded canonical Run identifier is required.", nameof(request));
    }
    private async Task PersistAsync(string operation, Func<FeatureInstallationState, FeatureInstallationState> transition)
    {
        using var activity = Start(operation);
        var next = Domain(() => transition(RequiredState()));
        if (ReferenceEquals(next, persistentState.State))
            return;
        await WriteAsync(next);
    }
    private async Task WriteAsync(FeatureInstallationState next)
    {
        await PersistedStateReconciliation.WriteWithRollbackAsync(persistentState, next, FeatureStateEquality.Same);
    }
    private async Task WriteReservationAsync(FeatureRuntimeReservationHold next)
    {
        await PersistedStateReconciliation.WriteWithRollbackAsync(
            reservationState,
            next,
            static (left, right) => left == right);
    }
    private FeatureRuntimeReservationSnapshot ReservationSnapshot()
    {
        var hold = reservationState.State;
        if (!HasReservation || hold is null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        if (!persistentState.RecordExists || persistentState.State is null)
            return new FeatureRuntimeReservationSnapshot(
                hold.Reservation,
                hold.Phase,
                false,
                null,
                null,
                null,
                null,
                null,
                null);
        var state = persistentState.State;
        return new FeatureRuntimeReservationSnapshot(
            hold.Reservation,
            hold.Phase,
            true,
            state.Revision,
            state.ActiveRelease,
            state.PreviousRelease,
            state.Paused,
            state.PauseReason,
            state.Lease is null
                ? null
                : new FeatureLeaseStatus(state.Lease.HostId, state.Lease.Fence, state.Lease.ExpiresAt, state.Lease.Attempt));
    }
    private FeatureRuntimeReservationHold DemandExactReservation(FeatureRuntimeReservation reservation)
    {
        if (!HasReservation || reservationState.State is not { } hold)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        if (hold.Reservation != reservation)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Conflict);
        return hold;
    }
    private FeatureInstallationState? DemandRecoverableMissingResetReservation(
        FeatureRuntimeReservation reservation,
        bool requireRuntimeAbsence)
    {
        if (reservation.RuntimeRevision is null)
        {
            if (persistentState.RecordExists)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            return null;
        }
        if (requireRuntimeAbsence || !persistentState.RecordExists)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var state = RequiredState();
        if (state.InstallationId != reservation.InstallationId ||
            state.ActiveRelease != reservation.RuntimeActiveRelease ||
            state.PreviousRelease != reservation.RuntimePreviousRelease ||
            state.UnconfirmedReleaseSwitch is not null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var exactPause = state.Paused == reservation.RuntimePaused &&
            string.Equals(state.PauseReason, reservation.RuntimePauseReason, StringComparison.Ordinal);
        var recoverableBackpressure = reservation.RuntimePaused == false && state.Paused &&
            string.Equals(state.PauseReason, "feature inbox full", StringComparison.Ordinal);
        if (!exactPause && !recoverableBackpressure)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        return state;
    }
    private void DemandReservationCoordinates(FeatureRuntimeReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        var (ownerId, installationId) = ParseKey();
        if (reservation.OwnerId != ownerId || reservation.InstallationId != installationId ||
            string.IsNullOrWhiteSpace(reservation.ActorId.Value) ||
            !IsDigest(reservation.ReservationToken) || !IsDigest(reservation.AccessDigest))
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var newInstallation = reservation.RuntimeRevision is null;
        if (newInstallation != (reservation.RuntimeActiveRelease is null) ||
            newInstallation != (reservation.RuntimePaused is null) ||
            newInstallation && (reservation.RuntimePreviousRelease is not null || reservation.RuntimePauseReason is not null) ||
            !newInstallation && reservation.RuntimeRevision < 0 ||
            reservation.RuntimePaused == false && reservation.RuntimePauseReason is not null ||
            reservation.RuntimePaused == true && string.IsNullOrWhiteSpace(reservation.RuntimePauseReason))
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    private void DemandActivatedReservation(FeatureRuntimeReservation reservation)
    {
        var state = RequiredState();
        if (reservation.RuntimeRevision is null)
        {
            DemandPristineReservedInitialization(state, reservation);
            return;
        }
        if (!IsExactReservationSwitch(state, reservation))
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    private static bool IsExactReservationSwitch(
        FeatureInstallationState state,
        FeatureRuntimeReservation reservation)
    {
        var releaseSwitch = state.UnconfirmedReleaseSwitch;
        if (releaseSwitch is null ||
            !string.Equals(releaseSwitch.OperationToken, reservation.ReservationToken, StringComparison.Ordinal) ||
            releaseSwitch.FromActiveRelease != reservation.RuntimeActiveRelease ||
            releaseSwitch.FromPreviousRelease != reservation.RuntimePreviousRelease ||
            releaseSwitch.ToRelease != reservation.CandidateRelease ||
            state.ActiveRelease != reservation.CandidateRelease || state.Revision != releaseSwitch.SwitchRevision)
            return false;
        var expectedPrevious = reservation.CandidateRelease == reservation.RuntimeActiveRelease
            ? reservation.RuntimePreviousRelease
            : reservation.RuntimeActiveRelease;
        return state.PreviousRelease == expectedPrevious;
    }
    private static void DemandPristineReservedInitialization(
        FeatureInstallationState state,
        FeatureRuntimeReservation reservation)
    {
        if (state.InstallationId != reservation.InstallationId || state.ActiveRelease != reservation.CandidateRelease ||
            state.PreviousRelease is not null || state.StateJson != "{}" || state.Paused ||
            state.Inbox.Length != 0 || state.Lease is not null || state.Completions.Length != 0 ||
            state.Intents.Length != 0 || state.NextFence != 0 || state.Revision != 0 ||
            state.PauseReason is not null || state.Schedules.Length != 0 || state.UnconfirmedReleaseSwitch is not null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    private static FeatureInstallationState NormalizeReservedBackpressure(
        FeatureInstallationState state,
        FeatureRuntimeReservation reservation) =>
        reservation.RuntimePaused == false && state.Paused &&
        string.Equals(state.PauseReason, "feature inbox full", StringComparison.Ordinal)
            ? state with
            {
                Paused = false,
                PauseReason = null,
                Revision = checked(state.Revision + 1)
            }
            : state;
    private static FeatureInstallationState NormalizeBackpressurePause(FeatureInstallationState state) =>
        state.UnconfirmedReleaseSwitch is null && state.Paused &&
        string.Equals(state.PauseReason, "feature inbox full", StringComparison.Ordinal)
            ? state with
            {
                Paused = false,
                PauseReason = null,
                Revision = checked(state.Revision + 1)
            }
            : state;
    private bool HasReservation => reservationState.RecordExists && reservationState.State is not null;
    private void DemandNoReservation()
    {
        if (HasReservation)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Conflict);
    }
    private static bool IsDigest(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
    private FeatureInstallationState RequiredState() =>
        persistentState.RecordExists && persistentState.State is not null
            ? persistentState.State
            : throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    private (BrainOwnerId OwnerId, FeatureInstallationId InstallationId) ParseKey() =>
        FeatureGrainIds.ParseInstallation(this.GetPrimaryKeyString());
    private Activity? Start(string operation, FeatureInput? input = null, string? correlationId = null, string? traceId = null)
    {
        var activity = ActivitySource.StartActivity(operation);
        activity?.SetTag("feature.grain_key", this.GetPrimaryKeyString());
        activity?.SetTag("feature.input_id", input?.InputId);
        activity?.SetTag("feature.correlation_id", correlationId ?? input?.CorrelationId);
        activity?.SetTag("feature.trace_id", traceId ?? input?.TraceId);
        return activity;
    }
    private static FeatureCompletionReceipt Receipt(FeatureCompletion completion) => new(completion.InputId, completion.Fence, completion.ResultJson, completion.CompletedAt, completion.CommitDigest, completion.InputDigest);
    private static FeatureIntentStatus IntentStatus(PersistedFeatureIntent intent) =>
        new(intent.OperationKey, intent.Kind, intent.PayloadJson, intent.AppliedAt, intent.InputId, intent.DeclinedAt);
    private static T Domain<T>(Func<T> transition)
    {
        try
        {
            return transition();
        }
        catch (FeatureConcurrencyException exception)
        {
            throw new FeatureCommandRejectedException(exception.Reason);
        }
        catch (FeatureLimitExceededException)
        {
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Limit);
        }
    }
}
