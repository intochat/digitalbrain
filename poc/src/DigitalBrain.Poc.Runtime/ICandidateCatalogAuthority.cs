namespace DigitalBrain.Poc.Runtime;

public interface ICandidateCatalogAuthority
{
    Task<CandidateCatalogRecord?> FindCandidateAsync(
        string candidateId,
        CancellationToken cancellationToken = default);

    Task IssueApprovalAsync(
        AuthenticatedPrincipal principal,
        CandidateCatalogRecord candidate,
        CancellationToken cancellationToken = default);

    Task<bool> ApprovalExistsAsync(
        string candidateId,
        CancellationToken cancellationToken = default);

    Task<CandidateCatalogRecord?> ActiveCandidateAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default);

    Task<CandidateCatalogRecord?> PreviousCandidateAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default);

    Task<bool> WasRolledBackAsync(
        AuthenticatedPrincipal principal,
        CandidateFamilyId family,
        string candidateSourceHash,
        CancellationToken cancellationToken = default);
}
