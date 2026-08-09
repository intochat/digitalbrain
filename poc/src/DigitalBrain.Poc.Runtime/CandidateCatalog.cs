namespace DigitalBrain.Poc.Runtime;

public sealed class CandidateCatalog
{
    private readonly ICandidateCatalogAuthority _authority;

    public CandidateCatalog(ICandidateCatalogAuthority authority)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    public async Task ApproveAsync(
        AuthenticatedPrincipal principal,
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        candidateId = candidateId.ToLowerInvariant();
        var candidate = await _authority.FindCandidateAsync(candidateId, cancellationToken) ??
            throw new KeyNotFoundException($"No trusted candidate exists for '{candidateId}'.");
        if (!string.Equals(candidate.OwnerId, principal.OwnerId, StringComparison.Ordinal))
        {
            throw new AuthorizationException("Only the owner bound by the signed attestation can approve the candidate.");
        }

        await _authority.IssueApprovalAsync(principal, candidate, cancellationToken);
    }

    public async Task<CandidateLifecycle> StatusAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        candidateId = candidateId.ToLowerInvariant();
        var candidate = await _authority.FindCandidateAsync(candidateId, cancellationToken) ??
            throw new KeyNotFoundException($"No trusted candidate exists for '{candidateId}'.");
        var principal = new AuthenticatedPrincipal(candidate.OwnerId);
        var active = await _authority.ActiveCandidateAsync(principal, candidate.Family, cancellationToken);
        if (active?.CandidateId == candidateId)
        {
            return CandidateLifecycle.Active;
        }

        var previous = await _authority.PreviousCandidateAsync(principal, candidate.Family, cancellationToken);
        if (previous?.CandidateId == candidateId)
        {
            return CandidateLifecycle.RolledBack;
        }

        if (await _authority.WasRolledBackAsync(
            principal,
            candidate.Family,
            candidate.SourceHash,
            cancellationToken))
        {
            return CandidateLifecycle.RolledBack;
        }

        return await _authority.ApprovalExistsAsync(candidateId, cancellationToken)
            ? CandidateLifecycle.ApprovedInactive
            : CandidateLifecycle.AwaitingOwnerApproval;
    }

    public Task<CandidateCatalogRecord?> ActiveAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default) =>
        _authority.ActiveCandidateAsync(principal, family, cancellationToken);

    public Task<CandidateCatalogRecord?> PreviousAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default) =>
        _authority.PreviousCandidateAsync(principal, family, cancellationToken);
}
