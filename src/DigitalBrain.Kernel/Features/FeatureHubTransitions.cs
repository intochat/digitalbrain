using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Kernel.Features;

internal static class FeatureHubTransitions
{
    public static FeatureHubState Propose(FeatureHubState state, FeatureReleaseProposal proposal, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(proposal);
        DemandRevision(state, expectedRevision);
        var release = ValidateRelease(proposal.Release);
        var grants = ValidateGrants(proposal.Grants);
        if (!grants.Select(grant => grant.CapabilityId).Order(StringComparer.Ordinal).SequenceEqual(release.RequestedCapabilities.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ArgumentException("The proposal must bind one grant for every requested capability.", nameof(proposal));
        var existingApproval = state.Approvals.FirstOrDefault(candidate =>
            candidate.InstallationId == proposal.InstallationId && candidate.Release.Digest == release.Digest);
        if (existingApproval is not null)
        {
            if (!SameRelease(existingApproval.Release, release) || !SameGrants(existingApproval.Grants, grants))
                throw new FeatureConcurrencyException("The release digest is already bound to different metadata.");
            return state;
        }
        var active = state.Authorities.FirstOrDefault(candidate =>
            candidate.InstallationId == proposal.InstallationId)?.ActiveRelease;
        var priorCapabilities = active is { } digest
            ? state.Releases.FirstOrDefault(candidate => candidate.Digest == digest)?.RequestedCapabilities ?? []
            : [];
        var added = release.RequestedCapabilities.Except(priorCapabilities, StringComparer.Ordinal).ToArray();
        var removed = priorCapabilities.Except(release.RequestedCapabilities, StringComparer.Ordinal).ToArray();
        var nextRevision = checked(state.Revision + 1);
        var approval = new FeatureApprovalState(
            ApprovalId(proposal.InstallationId, release.Digest, nextRevision),
            proposal.InstallationId,
            release,
            added,
            removed,
            FeatureApprovalStatus.Pending,
            null,
            null,
            nextRevision,
            grants);
        var releases = state.Releases.Any(candidate => candidate.Digest == release.Digest)
            ? state.Releases
            : [.. state.Releases, release];
        return state with { Releases = releases, Approvals = [.. state.Approvals, approval], Revision = nextRevision };
    }
    public static FeatureHubState Decide(FeatureHubState state, FeatureApprovalDecision decision, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(decision);
        DemandRevision(state, expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(decision.DecisionId);
        var index = Array.FindIndex(state.Approvals, candidate =>
            string.Equals(candidate.ApprovalId, decision.ApprovalId, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException("The feature approval does not exist.");
        var approval = state.Approvals[index];
        if (approval.Release.Digest != decision.Release)
            throw new FeatureConcurrencyException("Approval is bound to another release digest.");
        if (approval.Status != FeatureApprovalStatus.Pending)
            throw new FeatureConcurrencyException("The feature approval already has a decision.");
        var nextRevision = checked(state.Revision + 1);
        var approvals = state.Approvals.ToArray();
        approvals[index] = approval with
        {
            Status = decision.Approved ? FeatureApprovalStatus.Approved : FeatureApprovalStatus.Rejected,
            DecisionId = decision.DecisionId,
            DecidedAt = now,
            Revision = nextRevision
        };
        return state with { Approvals = approvals, Revision = nextRevision };
    }
    public static FeatureHubState Grant(FeatureHubState state, FeatureGrantRequest request, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        DemandRevision(state, expectedRevision);
        var approval = state.Approvals.LastOrDefault(candidate =>
            candidate.InstallationId == request.InstallationId && candidate.Release.Digest == request.Release &&
            candidate.Status == FeatureApprovalStatus.Approved)
            ?? throw new FeatureConcurrencyException("The exact release digest has not been approved.");
        var grants = ValidateGrants(request.Grants);
        if (!SameGrants(grants, approval.Grants))
            throw new FeatureConcurrencyException("The exact approved capability grants are required.");
        var index = Array.FindIndex(state.Authorities, candidate =>
            candidate.InstallationId == request.InstallationId);
        var current = index >= 0 ? state.Authorities[index] : null;
        var greatestRevision = new[]
        {
            current?.ActiveGrantRevision?.Value ?? 0,
            current?.PreviousGrantRevision?.Value ?? 0,
            current?.PendingGrantRevision?.Value ?? 0
        }.Max();
        var authority = (current ?? new FeatureInstallationAuthorityState(request.InstallationId, request.ActorId, null, null, null, [], null, [], null, null, [], false, null)) with
        {
            ActorId = request.ActorId,
            PendingRelease = request.Release,
            PendingGrantRevision = new GrantRevision(checked(greatestRevision + 1)),
            PendingGrants = grants
        };
        var authorities = state.Authorities.ToArray();
        if (index >= 0) authorities[index] = authority;
        else authorities = [.. authorities, authority];
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState Activate(FeatureHubState state, FeatureInstallationId installationId, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandRevision(state, expectedRevision);
        var index = AuthorityIndex(state, installationId);
        var authority = state.Authorities[index];
        if (authority.PendingRelease is not { } pendingRelease || authority.PendingGrantRevision is not { } pendingRevision)
            throw new FeatureConcurrencyException("The installation has no approved grant set staged.");
        var authorities = state.Authorities.ToArray();
        authorities[index] = authority with
        {
            PreviousRelease = authority.ActiveRelease,
            PreviousGrantRevision = authority.ActiveGrantRevision,
            PreviousGrants = authority.ActiveGrants,
            ActiveRelease = pendingRelease,
            ActiveGrantRevision = pendingRevision,
            ActiveGrants = authority.PendingGrants,
            PendingRelease = null,
            PendingGrantRevision = null,
            PendingGrants = []
        };
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState PauseAuthority(FeatureHubState state, FeatureInstallationId installationId, string reason, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandRevision(state, expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 512 || reason.Any(char.IsControl))
            throw new ArgumentException("A bounded safe pause reason is required.", nameof(reason));
        var index = AuthorityIndex(state, installationId);
        if (state.Authorities[index].Paused && string.Equals(state.Authorities[index].PauseReason, reason, StringComparison.Ordinal))
            return state;
        var authorities = state.Authorities.ToArray();
        authorities[index] = authorities[index] with { Paused = true, PauseReason = reason };
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState ResumeAuthority(FeatureHubState state, FeatureInstallationId installationId, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandRevision(state, expectedRevision);
        var index = AuthorityIndex(state, installationId);
        if (!state.Authorities[index].Paused) return state;
        var authorities = state.Authorities.ToArray();
        authorities[index] = authorities[index] with { Paused = false, PauseReason = null };
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState Revoke(FeatureHubState state, FeatureGrantRevocation revocation, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(revocation);
        DemandRevision(state, expectedRevision);
        var index = AuthorityIndex(state, revocation.InstallationId);
        var authority = state.Authorities[index];
        var next = RemoveGrant(authority, revocation);
        if (ReferenceEquals(next, authority)) return state;
        var authorities = state.Authorities.ToArray();
        authorities[index] = next;
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState RollbackAuthority(FeatureHubState state, FeatureInstallationId installationId, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandRevision(state, expectedRevision);
        var index = AuthorityIndex(state, installationId);
        var authority = state.Authorities[index];
        if (authority.PreviousRelease is not { } previousRelease || authority.PreviousGrantRevision is not { } previousRevision)
            return state;
        var authorities = state.Authorities.ToArray();
        authorities[index] = authority with
        {
            ActiveRelease = previousRelease,
            ActiveGrantRevision = previousRevision,
            ActiveGrants = authority.PreviousGrants,
            PreviousRelease = authority.ActiveRelease,
            PreviousGrantRevision = authority.ActiveGrantRevision,
            PreviousGrants = authority.ActiveGrants
        };
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureGrantState? ReadGrant(FeatureHubState state, FeatureGrantLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(lookup);
        var authority = state.Authorities.FirstOrDefault(candidate =>
            candidate.InstallationId == lookup.InstallationId);
        if (authority is null || authority.Paused) return null;
        if (authority.ActiveRelease == lookup.Release)
            return FindGrant(authority.ActiveGrants, lookup);
        return authority.PreviousRelease == lookup.Release ? FindGrant(authority.PreviousGrants, lookup) : null;
    }
    public static FeatureHubState Register(FeatureHubState state, FeatureInstallationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.InstallationId.Value) || string.IsNullOrWhiteSpace(registration.Release.Value))
            throw new ArgumentException("A complete feature installation registration is required.", nameof(registration));
        ArgumentNullException.ThrowIfNull(registration.Subscriptions);
        if (registration.Subscriptions.Length == 0 ||
            registration.Subscriptions.Any(subscription =>
                string.IsNullOrWhiteSpace(subscription) || subscription.Length > 256 || subscription.Any(char.IsControl) ||
                !string.Equals(subscription, subscription.Trim(), StringComparison.Ordinal)) ||
            registration.Subscriptions.Distinct(StringComparer.Ordinal).Count() != registration.Subscriptions.Length)
            throw new ArgumentException("Canonical unique feature subscriptions are required.", nameof(registration));
        var existing = Array.FindIndex(
            state.Installations,
            candidate => candidate.InstallationId == registration.InstallationId);
        if (existing >= 0)
        {
            var replaced = state.Installations.ToArray();
            replaced[existing] = registration;
            return state with { Installations = replaced, Revision = checked(state.Revision + 1) };
        }
        if (state.Installations.Length >= FeatureLimits.InstallationsPerOwner)
            throw new FeatureLimitExceededException("An owner can have at most 100 feature installations.");
        return state with { Installations = [.. state.Installations, registration], Revision = checked(state.Revision + 1) };
    }
    public static FeatureCreateDraftTransition CreateDraft(FeatureHubState state, string ownerScope, CreateFeatureDraft request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerScope);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        if (request.OperationId.Length > FeatureLimits.DraftOperationIdCharacters || request.OperationId.Any(char.IsControl))
            throw new ArgumentException("A bounded canonical operation id is required.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Goal);
        if (request.Goal.Length > FeatureLimits.DraftGoalCharacters || request.Goal.Any(char.IsControl))
            throw new ArgumentException("A bounded control-character-free feature draft goal is required.", nameof(request));
        if (request.RequestedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Feature draft timestamps must be UTC.", nameof(request));
        var drafts = state.Drafts ?? [];
        var existing = drafts.FirstOrDefault(draft => string.Equals(draft.OperationId, request.OperationId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!string.Equals(existing.Goal, request.Goal, StringComparison.Ordinal))
                throw new FeatureConcurrencyException("The operation id is already bound to a different feature draft goal.");
            return new FeatureCreateDraftTransition(state, existing);
        }
        if (drafts.Length >= FeatureLimits.DraftsPerOwner)
            throw new FeatureLimitExceededException("An owner can have at most 100 feature drafts.");
        var draft = new FeatureDraftProposal(DraftProposalId(ownerScope, request.OperationId), request.OperationId, request.Goal, "draft", request.RequestedAt);
        var nextState = state with { Drafts = [.. drafts, draft], Revision = checked(state.Revision + 1) };
        return new FeatureCreateDraftTransition(nextState, draft);
    }
    public static FeatureHubState BeginFanOut(FeatureHubState state, FeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        FeatureInstallationTransitions.ValidateInput(input);
        var existing = state.FanOuts.FirstOrDefault(batch =>
            string.Equals(batch.Input.InputId, input.InputId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!string.Equals(FeatureInstallationTransitions.InputDigest(existing.Input), FeatureInstallationTransitions.InputDigest(input), StringComparison.Ordinal))
                throw new FeatureConcurrencyException("The fan-out input id is already bound to different content.");
            return state;
        }
        var deliveries = state.Installations.Where(registration => registration.Subscriptions.Any(
                subscription => string.Equals(subscription, input.Kind, StringComparison.Ordinal)))
            .Select(registration => new FeatureFanOutDeliveryState(registration.InstallationId, false))
            .ToArray();
        var batch = new FeatureFanOutState(input, deliveries);
        var retained = state.FanOuts;
        if (retained.Length >= FeatureLimits.FanOutBatches)
        {
            var completedIndex = Array.FindIndex(
                retained,
                candidate => candidate.Deliveries.All(delivery => delivery.Delivered));
            if (completedIndex < 0)
                throw new FeatureLimitExceededException("Pending feature fan-out exceeds the durable ledger capacity.");
            retained = retained.Where((_, index) => index != completedIndex).ToArray();
        }
        FeatureFanOutState[] fanOuts = [.. retained, batch];
        return state with { FanOuts = fanOuts, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState RecordDeliveries(FeatureHubState state, string inputId, IReadOnlySet<FeatureInstallationId> delivered)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputId);
        ArgumentNullException.ThrowIfNull(delivered);
        var index = Array.FindIndex(
            state.FanOuts,
            batch => string.Equals(batch.Input.InputId, inputId, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException("The feature fan-out batch does not exist.");
        var batch = state.FanOuts[index];
        var deliveries = batch.Deliveries.Select(delivery =>
            delivery.Delivered || delivered.Contains(delivery.InstallationId) ? delivery with { Delivered = true } : delivery).ToArray();
        if (deliveries.SequenceEqual(batch.Deliveries))
            return state;
        var fanOuts = state.FanOuts.ToArray();
        fanOuts[index] = batch with { Deliveries = deliveries };
        return state with { FanOuts = fanOuts, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState RecordDeliveryOutcomes(FeatureHubState state, string inputId, IReadOnlyList<FeatureDeliveryAttempt> attempts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Feature delivery timestamps must be UTC.", nameof(now));
        var delivered = attempts.Where(attempt => attempt.Status is FeatureAppendStatus.Accepted or FeatureAppendStatus.Duplicate)
            .Select(attempt => attempt.InstallationId)
            .ToHashSet();
        var next = RecordDeliveries(state, inputId, delivered);
        var full = attempts.Where(attempt => attempt.Status == FeatureAppendStatus.Full)
            .Select(attempt => attempt.InstallationId)
            .Distinct()
            .ToArray();
        if (full.Length == 0) return next;
        var batch = next.FanOuts.Single(candidate =>
            string.Equals(candidate.Input.InputId, inputId, StringComparison.Ordinal));
        var alerts = next.Alerts.ToList();
        foreach (var installationId in full)
        {
            if (alerts.Any(alert => alert.InstallationId == installationId && string.Equals(alert.InputId, inputId, StringComparison.Ordinal)))
                continue;
            alerts.Add(new FeatureBackpressureAlert(installationId, inputId, batch.Input.Kind, now, "feature inbox full"));
        }
        if (alerts.Count > FeatureLimits.FanOutBatches)
            alerts = alerts.TakeLast(FeatureLimits.FanOutBatches).ToList();
        var authorities = next.Authorities.Select(authority =>
            full.Contains(authority.InstallationId) ? authority with { Paused = true, PauseReason = "feature inbox full" } : authority).ToArray();
        return next with { Alerts = alerts.ToArray(), Authorities = authorities, Revision = checked(next.Revision + 1) };
    }
    private static FeatureReleaseMetadata ValidateRelease(FeatureReleaseMetadata release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!release.SourceReference.StartsWith("sha256:", StringComparison.Ordinal) || release.SourceReference.Length != 71 ||
            !release.SourceReference.AsSpan(7).ToArray().All(Uri.IsHexDigit))
            throw new ArgumentException("A content-addressed source reference is required.", nameof(release));
        var capabilities = CanonicalValues(release.RequestedCapabilities, "capability");
        var dependencies = CanonicalValues(release.Dependencies, "dependency");
        return release with { RequestedCapabilities = capabilities, Dependencies = dependencies };
    }
    private static FeatureGrantState[] ValidateGrants(FeatureGrantSpec[] grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (grants.Length > 32)
            throw new ArgumentException("A release cannot request more than 32 capabilities.", nameof(grants));
        var seen = new HashSet<(string, int)>();
        return grants.Select(grant =>
        {
            ArgumentNullException.ThrowIfNull(grant);
            if (string.IsNullOrWhiteSpace(grant.CapabilityId) || grant.CapabilityId.Length > 256 || grant.CapabilityId.Any(char.IsControl) || grant.CapabilityVersion < 1 ||
                !seen.Add((grant.CapabilityId, grant.CapabilityVersion)))
                throw new ArgumentException("Canonical unique capability grants are required.", nameof(grants));
            if (Encoding.UTF8.GetByteCount(grant.ConstraintsJson) > 65_536)
                throw new ArgumentException("Capability constraints exceed 64 KiB.", nameof(grants));
            try
            {
                using var document = JsonDocument.Parse(grant.ConstraintsJson);
                var constraints = CapabilityGrantConstraintPolicy.CopyValidated(document.RootElement);
                if (!CapabilityGrantConstraintPolicy.AllowsTool(constraints, grant.CapabilityId))
                    throw new ArgumentException("Capability constraints must allow the exact granted capability.", nameof(grants));
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Capability constraints must be a bounded JSON object.", nameof(grants), exception);
            }
            return new FeatureGrantState(
                grant.CapabilityId,
                grant.CapabilityVersion,
                grant.ProviderConnectionId,
                grant.ConstraintsJson,
                ValidateProvider(grant.Provider, grant.ProviderConnectionId));
        }).OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal)
            .ThenBy(grant => grant.CapabilityVersion)
            .ToArray();
    }
    private static bool SameGrants(IReadOnlyList<FeatureGrantState> left, IReadOnlyList<FeatureGrantState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.CapabilityId == pair.Second.CapabilityId && pair.First.CapabilityVersion == pair.Second.CapabilityVersion &&
            pair.First.ProviderConnectionId == pair.Second.ProviderConnectionId &&
            string.Equals(pair.First.ConstraintsJson, pair.Second.ConstraintsJson, StringComparison.Ordinal) &&
            string.Equals(pair.First.Provider, pair.Second.Provider, StringComparison.Ordinal));
    private static bool SameRelease(FeatureReleaseMetadata left, FeatureReleaseMetadata right) =>
        left.Digest == right.Digest && string.Equals(left.SourceReference, right.SourceReference, StringComparison.Ordinal) &&
        left.SourceKind == right.SourceKind &&
        left.RequestedCapabilities.SequenceEqual(right.RequestedCapabilities, StringComparer.Ordinal) &&
        left.Dependencies.SequenceEqual(right.Dependencies, StringComparer.Ordinal);
    private static string? ValidateProvider(string? provider, ProviderConnectionId? connection)
    {
        if (provider is null)
        {
            if (connection is not null)
                throw new ArgumentException("A provider key is required for a provider connection.", nameof(provider));
            return null;
        }
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 64 || provider.Any(char.IsControl) ||
            !string.Equals(provider, provider.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical provider key is required.", nameof(provider));
        return provider;
    }
    private static string[] CanonicalValues(string[] values, string kind)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length > 64 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) || !string.Equals(value, value.Trim(), StringComparison.Ordinal)) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException($"Canonical unique {kind} identifiers are required.", nameof(values));
        return values.Order(StringComparer.Ordinal).ToArray();
    }
    private static string ApprovalId(FeatureInstallationId installationId, ReleaseDigest release, long revision) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"digitalbrain.v3.feature-approval\0{installationId.Value}\0{release.Value}\0{revision}")));
    private static string DraftProposalId(string ownerScope, string operationId) =>
        "proposal-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ownerScope + "\0" + operationId)))[..32];
    private static void DemandRevision(FeatureHubState state, long expectedRevision)
    {
        if (state.Revision != expectedRevision)
            throw new FeatureConcurrencyException("The feature hub revision changed.");
    }
    private static int AuthorityIndex(FeatureHubState state, FeatureInstallationId installationId)
    {
        var index = Array.FindIndex(state.Authorities, candidate => candidate.InstallationId == installationId);
        return index >= 0 ? index : throw new KeyNotFoundException("The feature installation authority does not exist.");
    }
    private static FeatureGrantState? FindGrant(FeatureGrantState[] grants, FeatureGrantLookup lookup) =>
        grants.FirstOrDefault(grant =>
            string.Equals(grant.CapabilityId, lookup.CapabilityId, StringComparison.Ordinal) &&
            grant.CapabilityVersion == lookup.CapabilityVersion);
    private static FeatureInstallationAuthorityState RemoveGrant(FeatureInstallationAuthorityState authority, FeatureGrantRevocation revocation)
    {
        var active = authority.ActiveRelease == revocation.Release
            ? authority.ActiveGrants.Where(grant => !Matches(grant, revocation)).ToArray()
            : authority.ActiveGrants;
        var previous = authority.PreviousRelease == revocation.Release
            ? authority.PreviousGrants.Where(grant => !Matches(grant, revocation)).ToArray()
            : authority.PreviousGrants;
        if (active.Length == authority.ActiveGrants.Length && previous.Length == authority.PreviousGrants.Length)
            return authority;
        var nextRevision = new GrantRevision(checked(new[]
        {
            authority.ActiveGrantRevision?.Value ?? 0,
            authority.PreviousGrantRevision?.Value ?? 0,
            authority.PendingGrantRevision?.Value ?? 0
        }.Max() + 1));
        return authority with
        {
            ActiveGrants = active,
            PreviousGrants = previous,
            ActiveGrantRevision = authority.ActiveRelease == revocation.Release ? nextRevision : authority.ActiveGrantRevision,
            PreviousGrantRevision = authority.PreviousRelease == revocation.Release ? nextRevision : authority.PreviousGrantRevision
        };
    }
    private static bool Matches(FeatureGrantState grant, FeatureGrantRevocation revocation) =>
        string.Equals(grant.CapabilityId, revocation.CapabilityId, StringComparison.Ordinal) &&
        grant.CapabilityVersion == revocation.CapabilityVersion;
}
