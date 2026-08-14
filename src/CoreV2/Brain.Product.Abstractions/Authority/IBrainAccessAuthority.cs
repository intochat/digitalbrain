using Brain.Product.Abstractions.Operations;

namespace Brain.Product.Abstractions.Authority;

public sealed class AuthorityAuthenticationEvidence
{
    public AuthorityAuthenticationEvidence(string scheme, string opaqueCredential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme, nameof(scheme));
        ArgumentException.ThrowIfNullOrWhiteSpace(opaqueCredential, nameof(opaqueCredential));
        Scheme = scheme;
        OpaqueCredential = opaqueCredential;
    }

    public string Scheme { get; }

    public string OpaqueCredential { get; }

    public override string ToString()
        => $"{nameof(AuthorityAuthenticationEvidence)} {{ Scheme = {Scheme}, OpaqueCredential = [REDACTED] }}";
}

public sealed class AuthorityAuthenticationRequest
{
    public AuthorityAuthenticationRequest(AuthorityAuthenticationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Evidence = evidence;
    }

    public AuthorityAuthenticationEvidence Evidence { get; }

    public override string ToString()
        => $"{nameof(AuthorityAuthenticationRequest)} {{ Evidence = {Evidence} }}";
}

public interface IBrainAccessAuthority
{
    Task<BrainAccessGrant> AuthenticateAsync(
        AuthorityAuthenticationRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkspacePresentation>> GetWorkspacePresentationsAsync(
        BrainAccessGrant accessGrant,
        CancellationToken cancellationToken);
}
