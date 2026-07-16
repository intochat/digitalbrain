using System.Diagnostics;
using Azure;
using DigitalBrain.Kernel.Contracts;
using Orleans.Runtime;
namespace DigitalBrain.Kernel.Features;

[GrainType("digitalbrain.v3.feature-hub")]
internal sealed class FeatureHubGrain(
    [PersistentState("feature-hub")] IPersistentState<FeatureHubState> persistentState,
    IGrainFactory grainFactory,
    TimeProvider timeProvider,
    IFeaturePublicationVerifier? publicationVerifier = null) : Grain, IFeatureHubGrain
{
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Features.Hub");
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (State.RequiresStorageRewrite)
            await WriteAsync(State with { RequiresStorageRewrite = false });
    }
    public async Task RegisterAsync(FeatureInstallationRegistration registration)
    {
        using var activity = Start("register");
        ArgumentNullException.ThrowIfNull(registration);
        var ownerId = ParseKey();
        Domain(() =>
        {
            FeatureHubTransitions.DemandNoInstallationReservationOrReset(State, registration.InstallationId);
            return true;
        });
        var next = Domain(() => FeatureHubTransitions.Register(State, registration));
        var installation = Resolver.Installation(ownerId, registration.InstallationId);
        await installation.InitializeAsync(registration.Release);
        await WriteAsync(next);
    }
    public async Task<FeatureFanOutResult> PublishAsync(FeatureInput input)
    {
        using var activity = Start("publish", input);
        var ownerId = ParseKey();
        var begun = Domain(() => FeatureHubTransitions.BeginFanOut(State, input));
        if (!ReferenceEquals(begun, persistentState.State))
        {
            await WriteAsync(begun);
        }
        var batch = State.FanOuts.Single(candidate =>
            string.Equals(candidate.Input.InputId, input.InputId, StringComparison.Ordinal));
        var dispatchable = batch with
        {
            Deliveries = batch.Deliveries
                .Where(delivery => !FeatureHubTransitions.HasInstallationReservationOrReset(State, delivery.InstallationId))
                .ToArray()
        };
        var attempts = await new FeatureFanOutDeliveryRail(Resolver).DispatchAsync(ownerId, dispatchable);
        var completed = FeatureHubTransitions.RecordDeliveryOutcomes(State, input.InputId, attempts, timeProvider.GetUtcNow());
        if (!ReferenceEquals(completed, persistentState.State))
        {
            await WriteAsync(completed);
        }
        return FanOutResult(State.FanOuts.Single(candidate =>
            string.Equals(candidate.Input.InputId, input.InputId, StringComparison.Ordinal)));
    }
    public Task<FeatureHubSnapshot> ReadAsync()
    {
        using var activity = Start("read");
        return Task.FromResult(new FeatureHubSnapshot(
            State.Installations.ToArray(),
            State.FanOuts.Select(FanOutResult).ToArray(),
            State.Revision,
            State.Releases.ToArray(),
            State.Approvals.Select(ApprovalSnapshot).ToArray(),
            State.Authorities.Select(authority => AuthoritySnapshot(State, authority)).ToArray(),
            State.Alerts.ToArray()));
    }
    public async Task<FeatureApprovalSnapshot> ProposeAsync(FeatureReleaseProposal proposal, long expectedRevision)
    {
        using var activity = Start("propose-release");
        var next = Domain(() => FeatureHubTransitions.Propose(State, proposal, expectedRevision, timeProvider.GetUtcNow()));
        if (!ReferenceEquals(next, State)) await WriteAsync(next);
        var grants = Domain(() => FeatureHubTransitions.ValidateGrants(proposal.Grants));
        return ApprovalSnapshot(next.Approvals.Single(candidate =>
            candidate.InstallationId == proposal.InstallationId && candidate.Release.Digest == proposal.Release.Digest &&
            candidate.Status != FeatureApprovalStatus.Superseded &&
            FeatureHubTransitions.SameRelease(candidate.Release, proposal.Release) &&
            FeatureHubTransitions.SameGrants(candidate.Grants, grants)));
    }
    public async Task<FeatureApprovalSnapshot> DecideAsync(FeatureApprovalDecision decision, long expectedRevision)
    {
        using var activity = Start("decide-release");
        var next = Domain(() => FeatureHubTransitions.Decide(State, decision, expectedRevision, timeProvider.GetUtcNow()));
        await WriteAsync(next);
        return ApprovalSnapshot(next.Approvals.Single(candidate =>
            string.Equals(candidate.ApprovalId, decision.ApprovalId, StringComparison.Ordinal)));
    }
    public async Task<FeatureAuthoritySnapshot> GrantAsync(FeatureGrantRequest request, long expectedRevision)
    {
        using var activity = Start("grant-release");
        var next = Domain(() => FeatureHubTransitions.Grant(State, request, expectedRevision));
        await WriteAsync(next);
        return AuthoritySnapshot(next, next.Authorities.Single(candidate =>
            candidate.InstallationId == request.InstallationId));
    }
    public async Task<FeatureAuthoritySnapshot> InstallAsync(FeatureInstallationRegistration registration, long expectedRevision)
    {
        using var activity = Start("install-release");
        Domain(() =>
        {
            FeatureHubTransitions.DemandExactReservedInstallation(State, registration);
            return true;
        });
        var activated = Domain(() => FeatureHubTransitions.Activate(State, registration.InstallationId, expectedRevision));
        var authority = activated.Authorities.Single(candidate =>
            candidate.InstallationId == registration.InstallationId);
        if (authority.ActiveRelease != registration.Release)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var registered = Domain(() => FeatureHubTransitions.Register(activated, registration));
        var ownerId = ParseKey();
        var installation = Resolver.Installation(ownerId, registration.InstallationId);
        var canonicalRegistration = registered.Installations.Single(candidate =>
            candidate.InstallationId == registration.InstallationId);
        var reservation = (State.DraftInstallationReservations ?? []).SingleOrDefault(candidate =>
            candidate.InstallationId == registration.InstallationId);
        if (reservation is not null)
            await installation.ActivateReservedReleaseAsync(RuntimeReservation(ownerId, reservation));
        else if (State.Installations.Any(candidate => candidate.InstallationId == registration.InstallationId))
            await installation.BeginReleaseSwitchAsync(
                registration.Release,
                FeatureInstallationReservationDigests.Access(
                    registration.InstallationId,
                    registration.Release,
                    authority.ActiveGrants.Select(GrantSpec).ToArray(),
                    canonicalRegistration.Subscriptions));
        else
            await installation.InitializeAsync(registration.Release);
        await WriteAsync(registered);
        return AuthoritySnapshot(registered, authority);
    }
    public async Task<FeaturePublicationTicket> PrepareActivePublicationAsync(FeatureInstallationId installationId)
    {
        using var activity = Start("prepare-active-publication");
        var authority = State.Authorities.SingleOrDefault(candidate => candidate.InstallationId == installationId)
            ?? throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var runtime = await Installation(installationId).ReadAsync();
        if (runtime.ActiveRelease != authority.ActiveRelease || runtime.Paused != authority.Paused ||
            !string.Equals(runtime.PauseReason, authority.PauseReason, StringComparison.Ordinal) ||
            runtime.Lease is not null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var reservation = (State.DraftInstallationReservations ?? []).SingleOrDefault(candidate =>
            candidate.InstallationId == installationId);
        var resetInProgress = (State.DraftInstallationResets ?? []).Any(candidate =>
            candidate.InstallationId == installationId);
        if (reservation is not null)
        {
            var expectedReservation = RuntimeReservation(ParseKey(), reservation);
            var hold = await Installation(installationId).ReadReservationAsync();
            var expectedPhase = resetInProgress
                ? FeatureRuntimeReservationPhase.Resetting
                : FeatureRuntimeReservationPhase.Switched;
            if (hold is null)
            {
                if (resetInProgress || !DemandExactConfirmedReservationReplay(State, reservation))
                    throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            }
            else if (hold.Reservation != expectedReservation || hold.Phase != expectedPhase)
            {
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            }
            if (hold is not null && !resetInProgress && reservation.AuthorityBaseline is { } baseline)
                DemandExactReservedReleaseSwitch(runtime, reservation, baseline, authority.ActiveRelease);
        }
        var prepared = Domain(() => FeaturePublicationTransitions.Prepare(State, installationId));
        if (!ReferenceEquals(prepared.State, State)) await WriteAsync(prepared.State);
        return prepared.Ticket;
    }
    internal static void DemandExactReservedReleaseSwitch(
        FeatureInstallationSnapshot runtime,
        FeatureDraftInstallationReservation reservation,
        FeatureInstallationAuthorityBaseline baseline,
        ReleaseDigest? activeRelease)
    {
        var releaseSwitch = runtime.UnconfirmedReleaseSwitch;
        var expectedPrevious = baseline.ActiveRelease == activeRelease
            ? baseline.PreviousRelease
            : baseline.ActiveRelease;
        if (releaseSwitch is null || reservation.RuntimeRevision is not { } runtimeRevision ||
            !string.Equals(releaseSwitch.OperationToken, reservation.CommandDigest, StringComparison.Ordinal) ||
            releaseSwitch.FromActiveRelease != baseline.ActiveRelease ||
            releaseSwitch.FromPreviousRelease != baseline.PreviousRelease ||
            releaseSwitch.ToRelease != activeRelease || releaseSwitch.FromRevision < runtimeRevision ||
            releaseSwitch.FromRevision == long.MaxValue ||
            releaseSwitch.SwitchRevision != releaseSwitch.FromRevision + 1 ||
            releaseSwitch.SwitchRevision > runtime.Revision || runtime.PreviousRelease != expectedPrevious)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    public async Task<FeaturePublicationReceipt> ConfirmActivePublicationAsync(FeaturePublicationReceipt receipt)
    {
        using var activity = Start("confirm-active-publication");
        if (receipt is null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        if (publicationVerifier is null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        var confirmed = Domain(() => FeaturePublicationTransitions.Confirm(State, receipt));
        try
        {
            await publicationVerifier.VerifyAsync(ParseKey(), confirmed.Ticket, receipt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureCommandRejectedException)
        {
            throw;
        }
        catch (FeatureAuthorityRejectedException)
        {
            throw;
        }
        catch (FeatureConcurrencyException exception)
        {
            throw new FeatureCommandRejectedException(exception.Reason);
        }
        catch (RequestFailedException)
        {
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        }
        catch (IOException)
        {
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        }
        catch (TimeoutException)
        {
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        }
        catch (OrleansException)
        {
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        }
        if (!ReferenceEquals(confirmed.State, State)) await WriteAsync(confirmed.State);
        var authority = confirmed.State.Authorities.Single(candidate =>
            candidate.InstallationId == receipt.InstallationId);
        var activeRelease = authority.ActiveRelease
            ?? throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var reservation = (confirmed.State.DraftInstallationReservations ?? []).SingleOrDefault(candidate =>
            candidate.InstallationId == receipt.InstallationId);
        var resetInProgress = (confirmed.State.DraftInstallationResets ?? []).Any(candidate =>
            candidate.InstallationId == receipt.InstallationId);
        if (reservation is not null && !resetInProgress)
        {
            await Installation(receipt.InstallationId).ReleaseReservationAsync(
                new FeatureRuntimeReservationRelease(
                    RuntimeReservation(ParseKey(), reservation),
                    FeatureRuntimeReservationPhase.Switched,
                    authority.ActiveRelease,
                    authority.PreviousRelease,
                    false));
        }
        else if (reservation is null)
        {
            await Installation(receipt.InstallationId).ConfirmReleaseSwitchAsync(activeRelease);
        }
        return confirmed.Receipt
            ?? throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    public async Task RevokeAsync(FeatureGrantRevocation revocation, long expectedRevision)
    {
        using var activity = Start("revoke-grant");
        var next = Domain(() => FeatureHubTransitions.Revoke(State, revocation, expectedRevision));
        if (!ReferenceEquals(next, State)) await WriteAsync(next);
    }
    public async Task PauseInstallationAsync(FeatureInstallationId installationId, string reason, long expectedRevision)
    {
        using var activity = Start("pause-installation");
        var next = Domain(() => FeatureHubTransitions.PauseAuthority(State, installationId, reason, expectedRevision));
        if (!ReferenceEquals(next, State)) await WriteAsync(next);
        await Installation(installationId).PauseAsync(reason);
    }
    public async Task ResumeInstallationAsync(FeatureInstallationId installationId, long expectedRevision)
    {
        using var activity = Start("resume-installation");
        var next = Domain(() => FeatureHubTransitions.ResumeAuthority(State, installationId, expectedRevision));
        await Installation(installationId).ResumeAsync();
        if (!ReferenceEquals(next, State)) await WriteAsync(next);
    }
    public Task<FeatureAuthoritySnapshot> RollbackInstallationAsync(FeatureInstallationId installationId, long expectedRevision)
    {
        using var activity = Start("rollback-installation");
        throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    public async Task<FeatureAuthoritySnapshot> RollbackInstallationExactAsync(RollbackFeatureInstallation command)
    {
        using var activity = Start("rollback-installation-exact");
        var rolledBack = Domain(() => FeatureHubTransitions.RollbackAuthority(State, command));
        await AlignRuntimeRollbackAsync(command);
        if (ReferenceEquals(rolledBack, State))
            return AuthoritySnapshot(State, State.Authorities.Single(candidate => candidate.InstallationId == command.InstallationId));
        var authority = rolledBack.Authorities.Single(candidate => candidate.InstallationId == command.InstallationId);
        await WriteAsync(rolledBack);
        return AuthoritySnapshot(rolledBack, authority);
    }
    public async Task<FeatureDraft> CreateDraftAsync(CreateFeatureDraft request)
    {
        using var activity = Start("create-draft");
        var result = Domain(() => FeatureHubTransitions.CreateDraft(State, this.GetPrimaryKeyString(), request));
        if (!ReferenceEquals(result.State, State)) await WriteAsync(result.State);
        return result.Draft;
    }
    public Task<FeatureDraft?> ReadDraftAsync(FeatureDraftId draftId)
    {
        using var activity = Start("read-draft");
        return Task.FromResult(Domain(() => FeatureDraftAuthoringTransitions.ReadDraft(State, draftId)));
    }
    public Task<FeatureDraft?> ReadInstalledDraftAsync(FeatureInstallationId installationId, ReleaseDigest release)
    {
        using var activity = Start("read-installed-draft");
        return Task.FromResult(Domain(() => FeatureDraftAuthoringTransitions.ReadInstalledDraft(State, installationId, release)));
    }
    public async Task<FeatureCapabilityProjection[]> ReadCapabilityCatalogAsync(ActorId actorId)
    {
        using var activity = Start("read-capability-catalog");
        var projected = FeatureHubTransitions.ProjectCapabilities(State, ParseKey(), actorId);
        var executable = new List<FeatureCapabilityProjection>(projected.Length);
        foreach (var projection in projected)
        {
            if (await HasExecutableRuntimeAsync(projection))
                executable.Add(projection);
        }
        return executable.ToArray();
    }
    public async Task<FeatureAppendStatus> StartFeatureRunAsync(StartFeatureRun command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var activity = Start("start-feature-run", command.Input);
        var ownerId = ParseKey();
        var projection = Domain(() => FeatureHubTransitions.DemandFeatureRun(State, ownerId, command));
        return await Resolver.Installation(ownerId, projection.InstallationId)
            .AppendExactAsync(projection.Release, command.Input);
    }
    public async Task<FeatureDraft> ReviseBehaviorAsync(ReviseFeatureBehavior command)
    {
        using var activity = Start("revise-behavior");
        var result = Domain(() => FeatureDraftAuthoringTransitions.ReviseBehavior(State, command));
        if (!ReferenceEquals(result.State, State)) await WriteAsync(result.State);
        return result.Draft;
    }
    public async Task<FeatureDraft> ReviseSourceAsync(ReviseFeatureSource command)
    {
        using var activity = Start("revise-source");
        var result = Domain(() => FeatureDraftAuthoringTransitions.ReviseSource(State, command));
        if (!ReferenceEquals(result.State, State)) await WriteAsync(result.State);
        return result.Draft;
    }
    public async Task<FeatureDraft> AcceptSuggestedChangeAsync(AcceptSuggestedChange command)
    {
        using var activity = Start("accept-suggested-change");
        var result = Domain(() => FeatureDraftAuthoringTransitions.AcceptSuggestedChange(State, command));
        if (!ReferenceEquals(result.State, State)) await WriteAsync(result.State);
        return result.Draft;
    }
    public Task<FeatureDraft> RejectSuggestedChangeAsync(RejectSuggestedChange command)
    {
        using var activity = Start("reject-suggested-change");
        return Task.FromResult(Domain(() => FeatureDraftAuthoringTransitions.RejectSuggestedChange(State, command).Draft));
    }
    public async Task<FeatureDraft> RecordVerificationAsync(RecordFeatureVerification command)
    {
        using var activity = Start("record-verification");
        var result = Domain(() => FeatureDraftAuthoringTransitions.RecordVerification(State, command));
        if (!ReferenceEquals(result.State, State)) await WriteAsync(result.State);
        return result.Draft;
    }
    public async Task<FeatureDraftInstallationReservation> AcquireDraftInstallationReservationAsync(InstallFeatureVersion command, ActorId actorId)
    {
        using var activity = Start("acquire-draft-installation-reservation");
        var result = Domain(() => FeatureDraftAuthoringTransitions.AcquireInstallationReservation(State, command, actorId));
        if (!ReferenceEquals(result.State, State)) await WriteAsync(result.State);
        var installation = Installation(result.Reservation.InstallationId);
        if (DemandExactConfirmedReservationReplay(State, result.Reservation))
        {
            var authority = State.Authorities.Single(candidate =>
                candidate.InstallationId == result.Reservation.InstallationId);
            var runtime = await installation.ReadAsync();
            if (runtime.ActiveRelease != authority.ActiveRelease || runtime.PreviousRelease != authority.PreviousRelease)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            if (await installation.ReadReservationAsync() is not null)
            {
                await installation.ReleaseReservationAsync(new FeatureRuntimeReservationRelease(
                    RuntimeReservation(ParseKey(), result.Reservation),
                    FeatureRuntimeReservationPhase.Switched,
                    authority.ActiveRelease,
                    authority.PreviousRelease,
                    false));
            }
        }
        else
        {
            await installation.EstablishReservationAsync(RuntimeReservation(ParseKey(), result.Reservation));
        }
        return result.Reservation;
    }
    public Task<FeatureDraftInstallationReservation?> ReadDraftInstallationReservationAsync(FeatureDraftId draftId)
    {
        using var activity = Start("read-draft-installation-reservation");
        return Task.FromResult(Domain(() => FeatureDraftAuthoringTransitions.ReadInstallationReservation(State, draftId)));
    }
    public Task<FeatureDraftInstallationResetObligation?> ReadDraftInstallationResetAsync(FeatureDraftId draftId)
    {
        using var activity = Start("read-draft-installation-reset");
        return Task.FromResult(Domain(() => FeatureDraftAuthoringTransitions.ReadInstallationReset(State, draftId)));
    }
    public async Task<FeatureDraftInstallationResetPreparation> ResetDraftInstallationReservationAsync(
        ResetFeatureDraftInstallationReservation command,
        ActorId actorId)
    {
        using var activity = Start("reset-draft-installation-reservation");
        var result = Domain(() => FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            State,
            command,
            actorId,
            timeProvider.GetUtcNow()));
        if (result.Replay)
            return new FeatureDraftInstallationResetPreparation(result.Draft, true, false, null);
        var reservation = State.DraftInstallationReservations?
            .Single(candidate => candidate.DraftId == command.DraftId) ??
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        var installationId = reservation.InstallationId;
        var installation = Installation(installationId);
        var runtimeReservation = RuntimeReservation(ParseKey(), reservation);
        await installation.ResetReservedReleaseAsync(runtimeReservation, result.RequiresRuntimeAbsence);
        if (result.PreservedAuthority is { } preserved)
        {
            var runtime = await installation.ReadAsync();
            if (reservation.RuntimeRevision is not { } baselineRevision ||
                reservation.RuntimeActiveRelease is not { } baselineActive ||
                baselineActive != preserved.ActiveRelease ||
                reservation.RuntimePreviousRelease != preserved.PreviousRelease)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            Domain(() => DemandPreservedRuntime(runtime, preserved, baselineRevision));
        }
        if (!result.RequiresRepublish)
        {
            await installation.ReleaseReservationAsync(new FeatureRuntimeReservationRelease(
                runtimeReservation,
                FeatureRuntimeReservationPhase.Resetting,
                result.PreservedAuthority?.ActiveRelease,
                result.PreservedAuthority?.PreviousRelease,
                result.PreservedAuthority is null));
        }
        if (!ReferenceEquals(result.State, State))
            await WriteAsync(result.State);
        var activeRegistration = result.PreservedAuthority is null
            ? null
            : result.State.Installations.Single(candidate => candidate.InstallationId == installationId);
        return new FeatureDraftInstallationResetPreparation(
            result.Draft,
            result.Completed,
            result.RequiresRepublish,
            activeRegistration);
    }
    public async Task<FeatureDraft> CompleteDraftInstallationReservationResetAsync(
        FeatureDraftId draftId,
        string idempotencyId,
        ActorId actorId)
    {
        using var activity = Start("complete-draft-installation-reservation-reset");
        var reservation = State.DraftInstallationReservations?
            .SingleOrDefault(candidate => candidate.DraftId == draftId);
        var result = Domain(() => FeatureDraftAuthoringTransitions.CompleteInstallationReservationReset(
            State,
            draftId,
            idempotencyId,
            actorId));
        if (!result.Replay)
        {
            var baseline = reservation?.AuthorityBaseline
                ?? throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
            await Installation(reservation.InstallationId).ReleaseReservationAsync(
                new FeatureRuntimeReservationRelease(
                    RuntimeReservation(ParseKey(), reservation),
                    FeatureRuntimeReservationPhase.Resetting,
                    baseline.ActiveRelease,
                    baseline.PreviousRelease,
                    false));
        }
        if (!ReferenceEquals(result.State, State))
            await WriteAsync(result.State);
        return result.Draft;
    }
    public async Task<FeatureDraft> MarkDraftInstalledAsync(MarkFeatureDraftInstalled command)
    {
        using var activity = Start("mark-draft-installed");
        var result = Domain(() => FeatureDraftAuthoringTransitions.MarkInstalled(State, command));
        if (!ReferenceEquals(result.State, State)) await WriteAsync(result.State);
        return result.Draft;
    }
    public Task<FeatureGrantSnapshot?> ReadGrantAsync(FeatureGrantLookup lookup)
    {
        using var activity = Start("read-grant");
        var grant = FeatureHubTransitions.ReadGrant(State, lookup);
        if (grant is null) return Task.FromResult<FeatureGrantSnapshot?>(null);
        var authority = State.Authorities.Single(candidate => candidate.InstallationId == lookup.InstallationId);
        var revision = authority.ActiveRelease == lookup.Release ? authority.ActiveGrantRevision : authority.PreviousGrantRevision;
        return Task.FromResult<FeatureGrantSnapshot?>(new FeatureGrantSnapshot(
            lookup.InstallationId,
            lookup.Release,
            GrantSpec(grant),
            revision ?? throw new InvalidOperationException("The matching grant revision is missing."),
            authority.ActorId,
            authority.Paused));
    }
    private FeatureHubState State =>
        persistentState.RecordExists && persistentState.State is not null ? persistentState.State : FeatureHubState.Empty;
    private BrainOwnerId ParseKey() => FeatureGrainIds.ParseHub(this.GetPrimaryKeyString());
    private IFeatureGrainResolver Resolver => new OrleansFeatureGrainResolver(grainFactory);
    private IFeatureInstallationGrain Installation(FeatureInstallationId installationId) =>
        Resolver.Installation(ParseKey(), installationId);
    private async Task<bool> HasExecutableRuntimeAsync(FeatureCapabilityProjection projection)
        => await FeatureRuntimeEligibility.IsExecutableAsync(
            Installation(projection.InstallationId),
            projection);
    private async Task AlignRuntimeRollbackAsync(RollbackFeatureInstallation command)
    {
        var installation = Installation(command.InstallationId);
        var current = await installation.ReadAsync();
        if (current.ActiveRelease == command.TargetRelease && current.PreviousRelease is null)
            return;
        if (current.ActiveRelease != command.ExpectedActiveRelease || current.PreviousRelease != command.TargetRelease)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        await installation.RollbackAsync();
        var restored = await installation.ReadAsync();
        if (restored.ActiveRelease != command.TargetRelease || restored.PreviousRelease is not null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    private async Task WriteAsync(FeatureHubState next)
    {
        await PersistedStateReconciliation.WriteWithRollbackAsync(persistentState, next, FeatureStateEquality.Same);
    }
    private static bool DemandPreservedRuntime(
        FeatureInstallationSnapshot runtime,
        FeatureInstallationAuthorityState authority,
        long expectedRevision)
    {
        if (runtime.InstallationId != authority.InstallationId || runtime.ActiveRelease != authority.ActiveRelease ||
            runtime.PreviousRelease != authority.PreviousRelease || runtime.Paused != authority.Paused ||
            runtime.Revision < expectedRevision ||
            !string.Equals(runtime.PauseReason, authority.PauseReason, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The active Feature runtime does not match the preserved authority.",
                FeatureCommandRejectionReason.Precondition);
        return true;
    }
    private Activity? Start(string operation, FeatureInput? input = null)
    {
        var activity = ActivitySource.StartActivity(operation);
        activity?.SetTag("feature.grain_key", this.GetPrimaryKeyString());
        activity?.SetTag("feature.input_id", input?.InputId);
        activity?.SetTag("feature.correlation_id", input?.CorrelationId);
        activity?.SetTag("feature.trace_id", input?.TraceId);
        return activity;
    }
    private static FeatureFanOutResult FanOutResult(FeatureFanOutState batch) => new(
        batch.Input.InputId,
        batch.Deliveries.Count(delivery => delivery.Delivered),
        batch.Deliveries.Count(delivery => !delivery.Delivered));
    private static FeatureApprovalSnapshot ApprovalSnapshot(FeatureApprovalState approval) => new(
        approval.ApprovalId,
        approval.InstallationId,
        approval.Release,
        approval.AddedCapabilities,
        approval.RemovedCapabilities,
        approval.Status,
        approval.DecisionId,
        approval.DecidedAt,
        approval.Revision,
        approval.Grants.Select(GrantSpec).ToArray(),
        approval.DecisionActorId);
    private static FeatureAuthoritySnapshot AuthoritySnapshot(FeatureHubState state, FeatureInstallationAuthorityState authority) => new(
        authority.InstallationId,
        authority.ActorId,
        authority.ActiveRelease,
        authority.PreviousRelease,
        authority.ActiveGrantRevision,
        authority.ActiveGrants.Select(GrantSpec).ToArray(),
        authority.PendingRelease,
        authority.PendingGrantRevision,
        authority.PendingGrants.Select(GrantSpec).ToArray(),
        authority.Paused,
        authority.PauseReason,
        authority.RollbackReplay is { } replay
            ? new FeatureRollbackReplaySnapshot(
                replay.InstallationId,
                replay.ExpectedActiveRelease,
                replay.TargetRelease,
                replay.ExpectedRevision,
                replay.IdempotencyId)
            : null,
        FeatureHubTransitions.ExactRollbackAvailable(authority),
        FeaturePublicationTransitions.IsConfirmedActive(state, authority.InstallationId));
    private static FeatureRuntimeReservation RuntimeReservation(
        BrainOwnerId ownerId,
        FeatureDraftInstallationReservation reservation)
    {
        var baseline = reservation.AuthorityBaseline;
        if (reservation.RuntimeRevision is not null && baseline is null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        return new FeatureRuntimeReservation(
            ownerId,
            reservation.DraftId,
            reservation.InstallationId,
            reservation.ActorId,
            reservation.CommandDigest,
            reservation.AccessDigest,
            reservation.Release,
            reservation.RuntimeRevision,
            reservation.RuntimeActiveRelease,
            reservation.RuntimePreviousRelease,
            baseline?.Paused,
            baseline?.PauseReason);
    }
    private static bool DemandExactConfirmedReservationReplay(
        FeatureHubState state,
        FeatureDraftInstallationReservation reservation)
    {
        var authority = state.Authorities.SingleOrDefault(candidate =>
            candidate.InstallationId == reservation.InstallationId);
        if (authority?.ActiveRelease != reservation.Release || authority.PublicationReceipt is null)
            return false;
        if (reservation.AuthorityBaseline is { } baseline &&
            authority.PublicationFence <= baseline.PublicationFence)
            return false;
        try
        {
            FeaturePublicationTransitions.DemandConfirmedReservation(state, reservation);
            return true;
        }
        catch (FeatureConcurrencyException exception)
        {
            throw new FeatureCommandRejectedException(exception.Reason);
        }
        catch (FeatureAuthorityRejectedException)
        {
            throw;
        }
    }
    private static FeatureGrantSpec GrantSpec(FeatureGrantState grant) => new(grant.CapabilityId, grant.CapabilityVersion, grant.ProviderConnectionId, grant.ConstraintsJson, grant.Provider);
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
