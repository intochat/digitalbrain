using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class FeaturePublicationTransitions
{
    public static FeaturePublicationTransition Prepare(
        FeatureHubState state,
        FeatureInstallationId installationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var authorityIndex = Array.FindIndex(state.Authorities, candidate => candidate.InstallationId == installationId);
        if (authorityIndex < 0)
            throw new KeyNotFoundException("The Feature installation authority was not found.");
        var authority = state.Authorities[authorityIndex];
        if (authority.ActiveRelease is not { } release || authority.ActiveGrantRevision is not { } grantRevision)
            throw new FeatureConcurrencyException(
                "The Feature installation has no active authority to publish.",
                FeatureCommandRejectionReason.Precondition);
        if (authority.PendingRelease is not null || authority.PendingGrantRevision is not null || authority.PendingGrants.Length != 0)
            throw new FeatureConcurrencyException(
                "A Feature publication cannot be prepared while another grant is pending.",
                FeatureCommandRejectionReason.Precondition);
        if (authority.Paused)
            throw new FeatureConcurrencyException(
                "A paused Feature installation cannot be published as active.",
                FeatureCommandRejectionReason.Precondition);
        var registrations = state.Installations.Where(candidate => candidate.InstallationId == installationId).ToArray();
        if (registrations.Length != 1 || registrations[0].Release != release)
            throw new FeatureConcurrencyException(
                "The active Feature registration does not match the authority release.",
                FeatureCommandRejectionReason.Precondition);
        var registration = registrations[0];
        var next = state;
        if (authority.PublicationFence == 0)
        {
            var authorities = state.Authorities.ToArray();
            authority = authority with { PublicationFence = 1, PublicationReceipt = null };
            authorities[authorityIndex] = authority;
            next = state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
        }
        else if (authority.PublicationFence < 0)
        {
            throw new FeatureConcurrencyException("The Feature publication fence is invalid.");
        }
        var subscriptions = registration.Subscriptions.Order(StringComparer.Ordinal).ToArray();
        var grants = authority.ActiveGrants
            .OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal)
            .ThenBy(grant => grant.CapabilityVersion)
            .Select(GrantSpec)
            .ToArray();
        var accessDigest = AccessDigest(installationId, release, authority.ActiveGrants, subscriptions);
        var authorityDigest = AuthorityDigest(authority, registration);
        return new FeaturePublicationTransition(
            next,
            new FeaturePublicationTicket(
                installationId,
                authority.ActorId,
                release,
                grantRevision,
                grants,
                subscriptions,
                authority.PublicationFence,
                authorityDigest,
                accessDigest),
            authority.PublicationReceipt);
    }

    public static FeaturePublicationTransition Confirm(
        FeatureHubState state,
        FeaturePublicationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        DemandDigest(receipt.AuthorityDigest, nameof(receipt.AuthorityDigest));
        DemandDigest(receipt.AccessDigest, nameof(receipt.AccessDigest));
        DemandDigest(receipt.ManifestDigest, nameof(receipt.ManifestDigest));
        if (receipt.PublicationFence < 1)
            throw new ArgumentOutOfRangeException(nameof(receipt), "A positive Feature publication fence is required.");
        var prepared = Prepare(state, receipt.InstallationId);
        if (prepared.Ticket.PublicationFence != receipt.PublicationFence ||
            !string.Equals(prepared.Ticket.AuthorityDigest, receipt.AuthorityDigest, StringComparison.Ordinal) ||
            !string.Equals(prepared.Ticket.AccessDigest, receipt.AccessDigest, StringComparison.Ordinal))
            throw new FeatureConcurrencyException("The Feature publication receipt is stale or conflicts with the active authority.");
        var authorityIndex = Array.FindIndex(prepared.State.Authorities, candidate => candidate.InstallationId == receipt.InstallationId);
        var authority = prepared.State.Authorities[authorityIndex];
        if (authority.PublicationReceipt is { } existing)
        {
            if (existing != receipt)
                throw new FeatureConcurrencyException("The Feature publication fence already has a different receipt.");
            return prepared with { Receipt = existing };
        }
        var authorities = prepared.State.Authorities.ToArray();
        authorities[authorityIndex] = authority with { PublicationReceipt = receipt };
        return prepared with
        {
            State = prepared.State with
            {
                Authorities = authorities,
                Revision = checked(prepared.State.Revision + 1)
            },
            Receipt = receipt
        };
    }

    public static string AccessDigest(
        FeatureInstallationId installationId,
        ReleaseDigest release,
        IReadOnlyList<FeatureGrantState> grants,
        IReadOnlyList<string> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(subscriptions);
        return FeatureInstallationReservationDigests.Access(
            installationId,
            release,
            grants.Select(GrantSpec).ToArray(),
            subscriptions);
    }

    public static FeatureInstallationAuthorityState Invalidate(FeatureInstallationAuthorityState authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.PublicationFence < 0)
            throw new FeatureConcurrencyException("The Feature publication fence is invalid.");
        return authority with
        {
            PublicationFence = checked(authority.PublicationFence + 1),
            PublicationReceipt = null
        };
    }

    public static void DemandConfirmedReservation(
        FeatureHubState state,
        FeatureDraftInstallationReservation reservation)
    {
        FeaturePublicationTransition prepared;
        try
        {
            prepared = Prepare(state, reservation.InstallationId);
        }
        catch (KeyNotFoundException)
        {
            throw new FeatureConcurrencyException(
                "The Feature installation has no confirmed active publication.",
                FeatureCommandRejectionReason.Precondition);
        }
        if (!ReferenceEquals(prepared.State, state))
            throw new FeatureConcurrencyException(
                "The Feature installation has no confirmed active publication.",
                FeatureCommandRejectionReason.Precondition);
        var receipt = prepared.Receipt
            ?? throw new FeatureConcurrencyException(
                "The Feature installation has no confirmed active publication.",
                FeatureCommandRejectionReason.Precondition);
        if (receipt.PublicationFence != prepared.Ticket.PublicationFence ||
            !string.Equals(receipt.AuthorityDigest, prepared.Ticket.AuthorityDigest, StringComparison.Ordinal) ||
            !string.Equals(receipt.AccessDigest, prepared.Ticket.AccessDigest, StringComparison.Ordinal) ||
            !string.Equals(receipt.AccessDigest, reservation.AccessDigest, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The confirmed Feature publication does not match the reserved access review.",
                FeatureCommandRejectionReason.Precondition);
        var authority = state.Authorities.Single(candidate => candidate.InstallationId == reservation.InstallationId);
        if (authority.ActorId != reservation.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        var reservationGrants = reservation.Grants is null
            ? throw new FeatureConcurrencyException(
                "The confirmed Feature publication cannot bind a legacy reservation without its exact access plan.",
                FeatureCommandRejectionReason.Precondition)
            : FeatureHubTransitions.ValidateGrants(reservation.Grants);
        var approvals = state.Approvals.Where(candidate =>
            candidate.InstallationId == reservation.InstallationId &&
            candidate.Release.Digest == reservation.Release &&
            candidate.Status == FeatureApprovalStatus.Approved &&
            string.Equals(candidate.DecisionId, reservation.DecisionId, StringComparison.Ordinal) &&
            candidate.DecisionActorId == reservation.ActorId &&
            FeatureHubTransitions.SameGrants(candidate.Grants, reservationGrants)).ToArray();
        if (approvals.Length != 1 || !SameGrants(approvals[0].Grants, authority.ActiveGrants))
            throw new FeatureConcurrencyException(
                "The confirmed Feature publication is not bound to the reserved approval decision.",
                FeatureCommandRejectionReason.Precondition);
    }

    public static void DemandConfirmedActive(FeatureHubState state, FeatureInstallationId installationId)
    {
        var prepared = Prepare(state, installationId);
        if (!ReferenceEquals(prepared.State, state) || prepared.Receipt is not { } receipt ||
            receipt.PublicationFence != prepared.Ticket.PublicationFence ||
            !string.Equals(receipt.AuthorityDigest, prepared.Ticket.AuthorityDigest, StringComparison.Ordinal) ||
            !string.Equals(receipt.AccessDigest, prepared.Ticket.AccessDigest, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The active Feature publication is not durably confirmed.",
                FeatureCommandRejectionReason.Precondition);
    }

    public static bool IsConfirmedActive(FeatureHubState state, FeatureInstallationId installationId)
    {
        try
        {
            var prepared = Prepare(state, installationId);
            return ReferenceEquals(prepared.State, state) &&
                   prepared.Receipt is { } receipt &&
                   receipt.PublicationFence == prepared.Ticket.PublicationFence &&
                   string.Equals(receipt.AuthorityDigest, prepared.Ticket.AuthorityDigest, StringComparison.Ordinal) &&
                   string.Equals(receipt.AccessDigest, prepared.Ticket.AccessDigest, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FeatureConcurrencyException)
        {
            return false;
        }
    }

    private static string AuthorityDigest(
        FeatureInstallationAuthorityState authority,
        FeatureInstallationRegistration registration)
    {
        var payload = new PublicationAuthority(
            authority.InstallationId.Value,
            authority.ActorId.Value,
            authority.ActiveRelease?.Value,
            authority.ActiveGrantRevision?.Value,
            GrantPayloads(authority.ActiveGrants),
            authority.PreviousRelease?.Value,
            authority.PreviousGrantRevision?.Value,
            GrantPayloads(authority.PreviousGrants),
            authority.PreviousSubscriptions?.Order(StringComparer.Ordinal).ToArray(),
            authority.PendingRelease?.Value,
            authority.PendingGrantRevision?.Value,
            GrantPayloads(authority.PendingGrants),
            registration.Release.Value,
            registration.Subscriptions.Order(StringComparer.Ordinal).ToArray(),
            authority.Paused,
            authority.PauseReason);
        return Digest(payload);
    }

    private static bool SameGrants(IReadOnlyList<FeatureGrantState> left, IReadOnlyList<FeatureGrantState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.CapabilityId == pair.Second.CapabilityId &&
            pair.First.CapabilityVersion == pair.Second.CapabilityVersion &&
            pair.First.ProviderConnectionId == pair.Second.ProviderConnectionId &&
            string.Equals(pair.First.ConstraintsJson, pair.Second.ConstraintsJson, StringComparison.Ordinal) &&
            string.Equals(pair.First.Provider, pair.Second.Provider, StringComparison.Ordinal));

    private static FeatureGrantSpec GrantSpec(FeatureGrantState grant) => new(
        grant.CapabilityId,
        grant.CapabilityVersion,
        grant.ProviderConnectionId,
        grant.ConstraintsJson,
        grant.Provider);

    private static PublicationGrant GrantPayload(FeatureGrantState grant) => new(
        grant.CapabilityId,
        grant.CapabilityVersion,
        grant.ProviderConnectionId?.Value,
        grant.ConstraintsJson,
        grant.Provider);

    private static PublicationGrant[] GrantPayloads(IEnumerable<FeatureGrantState> grants) => grants
        .OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal)
        .ThenBy(grant => grant.CapabilityVersion)
        .Select(GrantPayload)
        .ToArray();

    private static string Digest<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static void DemandDigest(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A canonical SHA-256 digest is required.", parameterName);
    }

    private sealed record PublicationAuthority(
        string InstallationId,
        string ActorId,
        string? ActiveRelease,
        long? ActiveGrantRevision,
        PublicationGrant[] ActiveGrants,
        string? PreviousRelease,
        long? PreviousGrantRevision,
        PublicationGrant[] PreviousGrants,
        string[]? PreviousSubscriptions,
        string? PendingRelease,
        long? PendingGrantRevision,
        PublicationGrant[] PendingGrants,
        string RegistrationRelease,
        string[] Subscriptions,
        bool Paused,
        string? PauseReason);

    private sealed record PublicationGrant(
        string CapabilityId,
        int CapabilityVersion,
        string? ProviderConnectionId,
        string ConstraintsJson,
        string? Provider);
}

internal sealed record FeaturePublicationTransition(
    FeatureHubState State,
    FeaturePublicationTicket Ticket,
    FeaturePublicationReceipt? Receipt);
