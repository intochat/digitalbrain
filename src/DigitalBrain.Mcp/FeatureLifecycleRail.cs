using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
namespace DigitalBrain.Mcp;

public sealed record FeatureInstallationInspection(FeatureAuthoritySnapshot Authority, FeatureInstallationRegistration? Registration, FeatureInstallationSnapshot? Runtime);
public sealed record FeatureLifecycleInspection(
    long Revision,
    IReadOnlyList<FeatureReleaseMetadata> Releases,
    IReadOnlyList<FeatureApprovalSnapshot> Approvals,
    IReadOnlyList<FeatureInstallationInspection> Installations,
    IReadOnlyList<FeatureInstallationRegistration> Registrations);
public interface IFeatureLifecycleRail
{
    Task<FeatureApprovalSnapshot> ProposeAsync(RuntimeRequestContext context, FeatureReleaseProposal proposal, long expectedRevision, CancellationToken cancellationToken = default);
    Task<FeatureApprovalSnapshot> DecideAsync(RuntimeRequestContext context, FeatureApprovalDecision decision, long expectedRevision, CancellationToken cancellationToken = default);
    Task<FeatureAuthoritySnapshot> GrantAsync(RuntimeRequestContext context, FeatureInstallationId installationId, ReleaseDigest release, FeatureGrantSpec[] grants, long expectedRevision, CancellationToken cancellationToken = default);
    Task<FeatureAuthoritySnapshot> InstallAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, long expectedRevision, CancellationToken cancellationToken = default);
    Task<FeatureAuthoritySnapshot> RollbackAsync(RuntimeRequestContext context, RollbackFeatureInstallation command, CancellationToken cancellationToken = default) =>
        Task.FromException<FeatureAuthoritySnapshot>(
            new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
    Task<FeatureAuthoritySnapshot> RepublishAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, CancellationToken cancellationToken = default);
    Task<FeatureLifecycleInspection> InspectAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default);
}
public sealed class FeatureLifecycleRail(IClusterClient cluster, FeatureArtifactPublisher artifacts, RuntimeSurfaceFeed surfaces) : IFeatureLifecycleRail
{
    public async Task<FeatureApprovalSnapshot> ProposeAsync(RuntimeRequestContext context, FeatureReleaseProposal proposal, long expectedRevision, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var published = await artifacts.DemandReleaseAsync(proposal.Release.Digest, cancellationToken);
        if (!SameRelease(published, proposal.Release))
            throw new InvalidDataException("The proposal metadata does not match the published Feature release.");
        var approval = await Hub(context).ProposeAsync(proposal, expectedRevision).WaitAsync(cancellationToken).ConfigureAwait(false);
        await surfaces.PublishFeatureApprovalAsync(context, approval, cancellationToken);
        return approval;
    }
    public async Task<FeatureApprovalSnapshot> DecideAsync(RuntimeRequestContext context, FeatureApprovalDecision decision, long expectedRevision, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        return await Hub(context).DecideAsync(decision with { ActorId = context.ActorId }, expectedRevision)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    public async Task<FeatureAuthoritySnapshot> GrantAsync(
        RuntimeRequestContext context,
        FeatureInstallationId installationId,
        ReleaseDigest release,
        FeatureGrantSpec[] grants,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        return await Hub(context).GrantAsync(new FeatureGrantRequest(installationId, release, context.ActorId, grants), expectedRevision).WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }
    public async Task<FeatureAuthoritySnapshot> InstallAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, long expectedRevision, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        registration = CanonicalRegistration(registration);
        var hub = Hub(context);
        var current = await hub.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var coordinateAuthority = current.Authorities.FirstOrDefault(candidate =>
            candidate.InstallationId == registration.InstallationId);
        if (coordinateAuthority is not null && coordinateAuthority.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        var existing = current.Authorities.FirstOrDefault(candidate =>
            candidate.InstallationId == registration.InstallationId && candidate.ActiveRelease == registration.Release);
        var installed = current.Installations.FirstOrDefault(candidate => candidate.InstallationId == registration.InstallationId);
        if (current.Revision != expectedRevision && existing is not null && installed is not null &&
            installed.Release == registration.Release && installed.Subscriptions.SequenceEqual(registration.Subscriptions, StringComparer.Ordinal))
        {
            DemandExactAuthority(context, existing, registration);
            await PublishCurrentAsync(context.OwnerId, hub, registration.InstallationId, cancellationToken);
            return existing;
        }
        var authority = await hub.InstallAsync(registration, expectedRevision).WaitAsync(cancellationToken).ConfigureAwait(false);
        DemandExactAuthority(context, authority, registration);
        await PublishCurrentAsync(context.OwnerId, hub, registration.InstallationId, cancellationToken);
        return authority;
    }
    public async Task RevokeAsync(RuntimeRequestContext context, FeatureGrantRevocation revocation, long expectedRevision, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        await Hub(context).RevokeAsync(revocation, expectedRevision).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task PauseAsync(
        RuntimeRequestContext context,
        FeatureInstallationId installationId,
        string reason,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        await Hub(context).PauseInstallationAsync(installationId, reason, expectedRevision).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task ResumeAsync(RuntimeRequestContext context, FeatureInstallationId installationId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        await Hub(context).ResumeInstallationAsync(installationId, expectedRevision).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    public Task<FeatureAuthoritySnapshot> RollbackAsync(RuntimeRequestContext context, FeatureInstallationId installationId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    public async Task<FeatureAuthoritySnapshot> RollbackAsync(RuntimeRequestContext context, RollbackFeatureInstallation command, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var hub = Hub(context);
        var authority = await hub.RollbackInstallationExactAsync(command).WaitAsync(cancellationToken).ConfigureAwait(false);
        await PublishCurrentAsync(context.OwnerId, hub, command.InstallationId, cancellationToken);
        return authority;
    }
    public async Task<FeatureAuthoritySnapshot> RepublishAsync(RuntimeRequestContext context, FeatureInstallationId installationId, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var hub = Hub(context);
        var snapshot = await hub.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var authority = snapshot.Authorities.FirstOrDefault(candidate => candidate.InstallationId == installationId && candidate.ActiveRelease is not null)
            ?? throw new KeyNotFoundException("The Feature installation has no active release to publish.");
        if (authority.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        await PublishCurrentAsync(context.OwnerId, hub, installationId, cancellationToken);
        return authority;
    }
    public async Task<FeatureAuthoritySnapshot> RepublishAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        registration = CanonicalRegistration(registration);
        var hub = Hub(context);
        var snapshot = await hub.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var authority = snapshot.Authorities.SingleOrDefault(candidate => candidate.InstallationId == registration.InstallationId)
            ?? throw new KeyNotFoundException("The Feature installation authority was not found.");
        DemandExactAuthority(context, authority, registration);
        var durableRegistration = snapshot.Installations.SingleOrDefault(candidate => candidate.InstallationId == registration.InstallationId);
        DemandSameRegistration(durableRegistration, registration);
        await PublishCurrentAsync(context.OwnerId, hub, registration.InstallationId, cancellationToken);
        snapshot = await hub.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        authority = snapshot.Authorities.SingleOrDefault(candidate => candidate.InstallationId == registration.InstallationId)
            ?? throw new KeyNotFoundException("The Feature installation authority was not found after publication.");
        DemandExactAuthority(context, authority, registration);
        durableRegistration = snapshot.Installations.SingleOrDefault(candidate => candidate.InstallationId == registration.InstallationId);
        DemandSameRegistration(durableRegistration, registration);
        return authority;
    }
    public async Task<FeatureLifecycleInspection> InspectAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var hub = await Hub(context).ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var inspections = new List<FeatureInstallationInspection>(hub.Authorities.Length);
        foreach (var authority in hub.Authorities)
        {
            var registration = hub.Installations.FirstOrDefault(candidate =>
                candidate.InstallationId == authority.InstallationId);
            FeatureInstallationSnapshot? runtime = null;
            if (registration is not null)
            {
                runtime = await cluster.GetGrain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(context.OwnerId, authority.InstallationId)).ReadAsync()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            inspections.Add(new FeatureInstallationInspection(authority, registration, runtime));
        }
        return new FeatureLifecycleInspection(hub.Revision, hub.Releases, hub.Approvals, inspections, hub.Installations);
    }
    private IFeatureHubGrain Hub(RuntimeRequestContext context) =>
        cluster.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
    private async Task PublishCurrentAsync(
        BrainOwnerId ownerId,
        IFeatureHubGrain hub,
        FeatureInstallationId installationId,
        CancellationToken cancellationToken)
    {
        var ticket = await hub.PrepareActivePublicationAsync(installationId).WaitAsync(cancellationToken).ConfigureAwait(false);
        var receipt = await FeaturePublicationRetry.ExecuteAsync(
            token => artifacts.PublishActiveAsync(ownerId, ticket, token),
            cancellationToken).ConfigureAwait(false);
        var confirmed = await hub.ConfirmActivePublicationAsync(receipt).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (confirmed != receipt)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    private static void DemandExactAuthority(RuntimeRequestContext context, FeatureAuthoritySnapshot authority, FeatureInstallationRegistration registration)
    {
        if (authority.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (authority.InstallationId != registration.InstallationId ||
            authority.ActiveRelease != registration.Release || authority.ActiveGrantRevision is null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    private static void DemandSameRegistration(FeatureInstallationRegistration? actual, FeatureInstallationRegistration expected)
    {
        if (actual is null || actual.InstallationId != expected.InstallationId || actual.Release != expected.Release ||
            !actual.Subscriptions.Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Subscriptions.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
    }
    private static FeatureInstallationRegistration CanonicalRegistration(FeatureInstallationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(registration.Subscriptions);
        return registration with { Subscriptions = registration.Subscriptions.Order(StringComparer.Ordinal).ToArray() };
    }
    private static bool SameRelease(FeatureReleaseMetadata left, FeatureReleaseMetadata right) =>
        left.Digest == right.Digest && string.Equals(left.SourceReference, right.SourceReference, StringComparison.Ordinal) &&
        left.SourceKind == right.SourceKind &&
        left.RequestedCapabilities.SequenceEqual(right.RequestedCapabilities, StringComparer.Ordinal) &&
        left.Dependencies.SequenceEqual(right.Dependencies, StringComparer.Ordinal) &&
        SameSource(left.Source, right.Source);
    private static bool SameSource(FeatureSourceSnapshot? left, FeatureSourceSnapshot? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        string.Equals(left.ImplementationProjectPath, right.ImplementationProjectPath, StringComparison.Ordinal) &&
        string.Equals(left.ScenarioProjectPath, right.ScenarioProjectPath, StringComparison.Ordinal) &&
        left.Files.SequenceEqual(right.Files);
    private static void DemandActor(RuntimeRequestContext context)
    {
        if (string.IsNullOrEmpty(context.OwnerId.Value) || string.IsNullOrEmpty(context.ActorId.Value))
            throw new UnauthorizedAccessException("An owner-scoped actor is required.");
    }
}
internal static class FeaturePublicationRetry
{
    private const int MaximumAttempts = 3;
    public static async Task ExecuteAsync(Func<CancellationToken, Task> publish, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publish);
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await publish(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (attempt < MaximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }
    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> publish, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publish);
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await publish(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (attempt < MaximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
