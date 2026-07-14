using System.Diagnostics;
using DigitalBrain.Kernel.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Kernel.Features;

[GrainType("digitalbrain.v3.feature-hub")]
public sealed class FeatureHubGrain(
    [PersistentState("feature-hub")] IPersistentState<FeatureHubState> persistentState,
    IGrainFactory grainFactory,
    TimeProvider timeProvider) : Grain, IFeatureHubGrain
{
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Features.Hub");

    public async Task RegisterAsync(FeatureInstallationRegistration registration)
    {
        using var activity = Start("register");
        ArgumentNullException.ThrowIfNull(registration);
        var ownerId = ParseKey();
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
        var attempts = await new FeatureFanOutDeliveryRail(Resolver).DispatchAsync(ownerId, batch);
        var completed = FeatureHubTransitions.RecordDeliveryOutcomes(
            State,
            input.InputId,
            attempts,
            timeProvider.GetUtcNow());
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
            State.Authorities.Select(AuthoritySnapshot).ToArray(),
            State.Alerts.ToArray()));
    }

    public async Task<FeatureApprovalSnapshot> ProposeAsync(
        FeatureReleaseProposal proposal,
        long expectedRevision)
    {
        using var activity = Start("propose-release");
        var next = Domain(() => FeatureHubTransitions.Propose(
            State,
            proposal,
            expectedRevision,
            timeProvider.GetUtcNow()));
        if (!ReferenceEquals(next, State)) await WriteAsync(next);
        return ApprovalSnapshot(next.Approvals.Single(candidate =>
            candidate.InstallationId == proposal.InstallationId &&
            candidate.Release.Digest == proposal.Release.Digest));
    }

    public async Task<FeatureApprovalSnapshot> DecideAsync(
        FeatureApprovalDecision decision,
        long expectedRevision)
    {
        using var activity = Start("decide-release");
        var next = Domain(() => FeatureHubTransitions.Decide(
            State,
            decision,
            expectedRevision,
            timeProvider.GetUtcNow()));
        await WriteAsync(next);
        return ApprovalSnapshot(next.Approvals.Single(candidate =>
            string.Equals(candidate.ApprovalId, decision.ApprovalId, StringComparison.Ordinal)));
    }

    public async Task<FeatureAuthoritySnapshot> GrantAsync(
        FeatureGrantRequest request,
        long expectedRevision)
    {
        using var activity = Start("grant-release");
        var next = Domain(() => FeatureHubTransitions.Grant(State, request, expectedRevision));
        await WriteAsync(next);
        return AuthoritySnapshot(next.Authorities.Single(candidate =>
            candidate.InstallationId == request.InstallationId));
    }

    public async Task<FeatureAuthoritySnapshot> InstallAsync(
        FeatureInstallationRegistration registration,
        long expectedRevision)
    {
        using var activity = Start("install-release");
        var activated = Domain(() => FeatureHubTransitions.Activate(State, registration.InstallationId, expectedRevision));
        var authority = activated.Authorities.Single(candidate =>
            candidate.InstallationId == registration.InstallationId);
        if (authority.ActiveRelease != registration.Release)
            throw new InvalidOperationException("The staged grant release does not match the installation release.");
        var registered = Domain(() => FeatureHubTransitions.Register(activated, registration));
        var ownerId = ParseKey();
        var installation = Resolver.Installation(ownerId, registration.InstallationId);
        if (State.Installations.Any(candidate => candidate.InstallationId == registration.InstallationId))
            await installation.SwitchReleaseAsync(registration.Release);
        else
            await installation.InitializeAsync(registration.Release);
        await WriteAsync(registered);
        return AuthoritySnapshot(authority);
    }

    public async Task RevokeAsync(FeatureGrantRevocation revocation, long expectedRevision)
    {
        using var activity = Start("revoke-grant");
        var next = Domain(() => FeatureHubTransitions.Revoke(State, revocation, expectedRevision));
        if (!ReferenceEquals(next, State)) await WriteAsync(next);
    }

    public async Task PauseInstallationAsync(
        FeatureInstallationId installationId,
        string reason,
        long expectedRevision)
    {
        using var activity = Start("pause-installation");
        var next = Domain(() => FeatureHubTransitions.PauseAuthority(
            State,
            installationId,
            reason,
            expectedRevision));
        if (!ReferenceEquals(next, State)) await WriteAsync(next);
        await Installation(installationId).PauseAsync(reason);
    }

    public async Task ResumeInstallationAsync(
        FeatureInstallationId installationId,
        long expectedRevision)
    {
        using var activity = Start("resume-installation");
        var next = Domain(() => FeatureHubTransitions.ResumeAuthority(State, installationId, expectedRevision));
        await Installation(installationId).ResumeAsync();
        if (!ReferenceEquals(next, State)) await WriteAsync(next);
    }

    public async Task<FeatureAuthoritySnapshot> RollbackInstallationAsync(
        FeatureInstallationId installationId,
        long expectedRevision)
    {
        using var activity = Start("rollback-installation");
        var rolledBack = Domain(() => FeatureHubTransitions.RollbackAuthority(State, installationId, expectedRevision));
        if (ReferenceEquals(rolledBack, State))
            return AuthoritySnapshot(State.Authorities.Single(candidate => candidate.InstallationId == installationId));
        var authority = rolledBack.Authorities.Single(candidate => candidate.InstallationId == installationId);
        var registration = rolledBack.Installations.Single(candidate => candidate.InstallationId == installationId) with
        {
            Release = authority.ActiveRelease ?? throw new InvalidOperationException("Rollback release is missing.")
        };
        var registered = FeatureHubTransitions.Register(rolledBack, registration);
        await Installation(installationId).RollbackAsync();
        await WriteAsync(registered);
        return AuthoritySnapshot(authority);
    }

    public Task<FeatureGrantSnapshot?> ReadGrantAsync(FeatureGrantLookup lookup)
    {
        using var activity = Start("read-grant");
        var grant = FeatureHubTransitions.ReadGrant(State, lookup);
        if (grant is null) return Task.FromResult<FeatureGrantSnapshot?>(null);
        var authority = State.Authorities.Single(candidate => candidate.InstallationId == lookup.InstallationId);
        var revision = authority.ActiveRelease == lookup.Release
            ? authority.ActiveGrantRevision
            : authority.PreviousGrantRevision;
        return Task.FromResult<FeatureGrantSnapshot?>(new FeatureGrantSnapshot(
            lookup.InstallationId,
            lookup.Release,
            GrantSpec(grant),
            revision ?? throw new InvalidOperationException("The matching grant revision is missing."),
            authority.ActorId,
            authority.Paused));
    }

    private FeatureHubState State =>
        persistentState.RecordExists && persistentState.State is not null
            ? persistentState.State
            : FeatureHubState.Empty;

    private BrainOwnerId ParseKey() => FeatureGrainIds.ParseHub(this.GetPrimaryKeyString());

    private IFeatureGrainResolver Resolver => new OrleansFeatureGrainResolver(grainFactory);

    private IFeatureInstallationGrain Installation(FeatureInstallationId installationId) =>
        Resolver.Installation(ParseKey(), installationId);

    private async Task WriteAsync(FeatureHubState next)
    {
        await PersistedStateReconciliation.WriteWithRollbackAsync(
            persistentState,
            next,
            FeatureStateEquality.Same);
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
        approval.Grants.Select(GrantSpec).ToArray());

    private static FeatureAuthoritySnapshot AuthoritySnapshot(FeatureInstallationAuthorityState authority) => new(
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
        authority.PauseReason);

    private static FeatureGrantSpec GrantSpec(FeatureGrantState grant) => new(
        grant.CapabilityId,
        grant.CapabilityVersion,
        grant.ProviderConnectionId,
        grant.ConstraintsJson,
        grant.Provider);

    private static T Domain<T>(Func<T> transition)
    {
        try
        {
            return transition();
        }
        catch (FeatureConcurrencyException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
        catch (FeatureLimitExceededException exception)
        {
            throw new InvalidOperationException(exception.Message);
        }
    }
}
