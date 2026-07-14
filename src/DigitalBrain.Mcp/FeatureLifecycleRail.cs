using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Contracts;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record FeatureInstallationInspection(FeatureAuthoritySnapshot Authority, FeatureInstallationRegistration? Registration, FeatureInstallationSnapshot? Runtime);

public sealed record FeatureLifecycleInspection(
    long Revision,
    IReadOnlyList<FeatureReleaseMetadata> Releases,
    IReadOnlyList<FeatureApprovalSnapshot> Approvals,
    IReadOnlyList<FeatureInstallationInspection> Installations);

public sealed class FeatureLifecycleRail(IClusterClient cluster, FeatureArtifactPublisher artifacts, RuntimeSurfaceFeed surfaces)
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
        return await Hub(context).DecideAsync(decision, expectedRevision).WaitAsync(cancellationToken).ConfigureAwait(false);
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
        var authority = await Hub(context).InstallAsync(registration, expectedRevision).WaitAsync(cancellationToken).ConfigureAwait(false);
        await artifacts.PublishActiveAsync(context.OwnerId, authority, cancellationToken);
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

    public async Task<FeatureAuthoritySnapshot> RollbackAsync(RuntimeRequestContext context, FeatureInstallationId installationId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        DemandActor(context);
        var authority = await Hub(context).RollbackInstallationAsync(installationId, expectedRevision).WaitAsync(cancellationToken).ConfigureAwait(false);
        await artifacts.PublishActiveAsync(context.OwnerId, authority, cancellationToken);
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
        return new FeatureLifecycleInspection(hub.Revision, hub.Releases, hub.Approvals, inspections);
    }

    private IFeatureHubGrain Hub(RuntimeRequestContext context) =>
        cluster.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));

    private static bool SameRelease(FeatureReleaseMetadata left, FeatureReleaseMetadata right) =>
        left.Digest == right.Digest && string.Equals(left.SourceReference, right.SourceReference, StringComparison.Ordinal) &&
        left.SourceKind == right.SourceKind &&
        left.RequestedCapabilities.SequenceEqual(right.RequestedCapabilities, StringComparer.Ordinal) &&
        left.Dependencies.SequenceEqual(right.Dependencies, StringComparer.Ordinal);

    private static void DemandActor(RuntimeRequestContext context)
    {
        if (string.IsNullOrEmpty(context.OwnerId.Value) || string.IsNullOrEmpty(context.ActorId.Value))
            throw new UnauthorizedAccessException("An owner-scoped actor is required.");
    }
}
