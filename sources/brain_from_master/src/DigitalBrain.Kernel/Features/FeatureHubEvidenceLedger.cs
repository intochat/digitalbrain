using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureHubEvidenceLedger
{
    public static void DemandOwnerCoordinateCapacity(
        FeatureHubState state,
        FeatureInstallationId installationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var coordinates = OwnerCoordinates(state);
        if (!coordinates.Contains(installationId) &&
            coordinates.Count >= FeatureLimits.InstallationsPerOwner)
            throw new FeatureLimitExceededException(
                "An Owner can have at most 100 Feature installation coordinates.");
    }

    public static void DemandCandidateAdmission(
        FeatureHubState state,
        FeatureInstallationId installationId,
        ReleaseDigest release)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Authorities.Any(candidate =>
                candidate.InstallationId == installationId &&
                candidate.PendingRelease is { } pendingRelease &&
                pendingRelease != release))
            throw new FeatureConcurrencyException(
                "The Feature installation already has a staged authority candidate.",
                FeatureCommandRejectionReason.Precondition);
        var protectedReleases = LifecycleReleases(state, installationId);
        if (state.Approvals.Any(candidate =>
                candidate.InstallationId == installationId &&
                candidate.Release.Digest != release &&
                candidate.Status is FeatureApprovalStatus.Pending or FeatureApprovalStatus.Approved &&
                !protectedReleases.Contains(candidate.Release.Digest)))
            throw new FeatureConcurrencyException(
                "The Feature installation already has an unbound approval candidate.",
                FeatureCommandRejectionReason.Precondition);
    }

    public static FeatureApprovalState[] AdmitProposal(
        FeatureHubState state,
        FeatureInstallationId installationId,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(state);
        var protectedReleases = LifecycleReleases(state, installationId);
        return state.Approvals
            .Select(approval =>
                approval.InstallationId == installationId &&
                approval.Status == FeatureApprovalStatus.Rejected &&
                !protectedReleases.Contains(approval.Release.Digest)
                    ? approval with
                    {
                        Status = FeatureApprovalStatus.Superseded,
                        Revision = revision
                    }
                    : approval)
            .ToArray();
    }

    public static FeatureHubState NormalizeLifecycleEvidence(
        FeatureHubState state,
        FeatureInstallationId installationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var protectedReleases = LifecycleReleases(state, installationId);
        if (state.Approvals.Any(approval =>
                approval.InstallationId == installationId &&
                approval.Status == FeatureApprovalStatus.Pending &&
                !protectedReleases.Contains(approval.Release.Digest)))
            throw new FeatureConcurrencyException(
                "The Feature installation has ambiguous pending approval evidence.",
                FeatureCommandRejectionReason.Precondition);
        var approvals = state.Approvals
            .Select(approval =>
                approval.InstallationId == installationId &&
                approval.Status is FeatureApprovalStatus.Approved or FeatureApprovalStatus.Rejected &&
                !protectedReleases.Contains(approval.Release.Digest)
                    ? approval with
                    {
                        Status = FeatureApprovalStatus.Superseded,
                        Revision = state.Revision
                    }
                    : approval)
            .ToArray();
        return CompactReleases(state with
        {
            Approvals = FeatureApprovalLedger.Compact(approvals)
        });
    }

    public static FeatureHubState CompactReleases(FeatureHubState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var retained = new HashSet<ReleaseDigest>();
        foreach (var approval in state.Approvals)
        {
            if (approval.Status != FeatureApprovalStatus.Superseded)
                retained.Add(approval.Release.Digest);
        }
        foreach (var registration in state.Installations)
            retained.Add(registration.Release);
        foreach (var authority in state.Authorities)
        {
            Add(retained, authority.ActiveRelease);
            Add(retained, authority.PreviousRelease);
            Add(retained, authority.PendingRelease);
        }
        foreach (var reservation in state.DraftInstallationReservations ?? [])
        {
            retained.Add(reservation.Release);
            Add(retained, reservation.RuntimeActiveRelease);
            Add(retained, reservation.RuntimePreviousRelease);
            if (reservation.AuthorityBaseline is { } baseline)
            {
                retained.Add(baseline.ActiveRelease);
                Add(retained, baseline.PreviousRelease);
                retained.Add(baseline.Registration.Release);
            }
        }
        foreach (var reset in state.DraftInstallationResets ?? [])
            retained.Add(reset.Release);
        return state with
        {
            Releases = state.Releases
                .Where(release => retained.Contains(release.Digest))
                .ToArray()
        };
    }

    private static HashSet<FeatureInstallationId> OwnerCoordinates(FeatureHubState state)
    {
        var coordinates = new HashSet<FeatureInstallationId>();
        foreach (var registration in state.Installations)
            coordinates.Add(registration.InstallationId);
        foreach (var authority in state.Authorities)
            coordinates.Add(authority.InstallationId);
        foreach (var reservation in state.DraftInstallationReservations ?? [])
            coordinates.Add(reservation.InstallationId);
        foreach (var reset in state.DraftInstallationResets ?? [])
            coordinates.Add(reset.InstallationId);
        foreach (var approval in state.Approvals)
        {
            if (approval.Status != FeatureApprovalStatus.Superseded)
                coordinates.Add(approval.InstallationId);
        }
        return coordinates;
    }

    private static HashSet<ReleaseDigest> LifecycleReleases(
        FeatureHubState state,
        FeatureInstallationId installationId)
    {
        var releases = new HashSet<ReleaseDigest>();
        foreach (var registration in state.Installations.Where(candidate =>
                     candidate.InstallationId == installationId))
            releases.Add(registration.Release);
        foreach (var authority in state.Authorities.Where(candidate =>
                     candidate.InstallationId == installationId))
        {
            Add(releases, authority.ActiveRelease);
            Add(releases, authority.PreviousRelease);
            Add(releases, authority.PendingRelease);
        }
        foreach (var reservation in (state.DraftInstallationReservations ?? []).Where(candidate =>
                     candidate.InstallationId == installationId))
        {
            releases.Add(reservation.Release);
            Add(releases, reservation.RuntimeActiveRelease);
            Add(releases, reservation.RuntimePreviousRelease);
            if (reservation.AuthorityBaseline is { } baseline)
            {
                releases.Add(baseline.ActiveRelease);
                Add(releases, baseline.PreviousRelease);
                releases.Add(baseline.Registration.Release);
            }
        }
        foreach (var reset in (state.DraftInstallationResets ?? []).Where(candidate =>
                     candidate.InstallationId == installationId))
            releases.Add(reset.Release);
        return releases;
    }

    private static void Add(HashSet<ReleaseDigest> releases, ReleaseDigest? release)
    {
        if (release is { } value)
            releases.Add(value);
    }
}
