namespace Brain.Product.Abstractions.Authority;

public sealed record AuthorityAuthenticationRequest
{
    public AuthorityAuthenticationRequest(
        string scheme,
        string opaqueCredential,
        string? nonAuthorizingPresentationRequest = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme, nameof(scheme));
        ArgumentException.ThrowIfNullOrWhiteSpace(opaqueCredential, nameof(opaqueCredential));
        Scheme = scheme;
        OpaqueCredential = opaqueCredential;
        NonAuthorizingPresentationRequest = nonAuthorizingPresentationRequest;
    }

    public string Scheme { get; }

    public string OpaqueCredential { get; }

    public string? NonAuthorizingPresentationRequest { get; }
}

public interface IBrainAccessAuthority
{
    Task<BrainAccessGrant> AuthenticateAsync(
        AuthorityAuthenticationRequest request,
        CancellationToken cancellationToken);
}
