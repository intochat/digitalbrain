using System.Globalization;
using System.Security.Claims;
using System.Text;
using Brain.Product.Abstractions.Authority;
using Brain.Product.Abstractions.Operations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DigitalBrain.ProductHost.Authority;

internal sealed class LocalTestAuthority : IBrainAccessAuthority
{
    public const string Issuer = "https://local-authority.digitalbrain.test";
    public const string Audience = "digitalbrain-product";
    private static readonly SymmetricSecurityKey SigningKey = new(
        Encoding.UTF8.GetBytes("digitalbrain-local-authority-test-key-v1-only-not-a-secret"));
    private static readonly SigningCredentials SigningCredentials =
        new(SigningKey, SecurityAlgorithms.HmacSha256);
    private static readonly SigningCredentials UntrustedSigningCredentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes("digitalbrain-untrusted-local-test-key-v1-not-a-secret")),
        SecurityAlgorithms.HmacSha256);
    private readonly OidcClaimsAuthority _authority;
    private readonly JsonWebTokenHandler _tokenHandler = new() { MapInboundClaims = false };
    private readonly TimeProvider _timeProvider;

    internal LocalTestAuthority(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _authority = new OidcClaimsAuthority(
            new AuthorityOptions(Issuer, Audience),
            new TokenValidationParameters { IssuerSigningKey = SigningKey },
            _timeProvider);
    }

    public Task<BrainAccessGrant> AuthenticateAsync(
        AuthorityAuthenticationRequest request,
        CancellationToken cancellationToken)
        => _authority.AuthenticateAsync(request, cancellationToken);

    public Task<IReadOnlyList<WorkspacePresentation>> GetWorkspacePresentationsAsync(
        BrainAccessGrant accessGrant,
        CancellationToken cancellationToken)
        => _authority.GetWorkspacePresentationsAsync(accessGrant, cancellationToken);

    internal AuthorityAuthenticationRequest Issue(
        string? workspace,
        string principal,
        IEnumerable<string> roles,
        IEnumerable<string> grants,
        IEnumerable<string> connections,
        int policyVersion = 1,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null,
        string? audience = null,
        IEnumerable<Claim>? additionalClaims = null,
        string? issuer = null,
        bool useUntrustedSigningKey = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(connections);

        var issued = issuedAt ?? _timeProvider.GetUtcNow();
        var expires = expiresAt ?? issued.AddMinutes(5);
        var claims = new List<Claim> { new(AuthorityOptions.DefaultSubjectClaim, principal) };
        if (workspace is not null)
        {
            claims.Add(new Claim(AuthorityOptions.DefaultWorkspaceClaim, workspace));
        }

        claims.AddRange(roles.Select(static value => new Claim(AuthorityOptions.DefaultRoleClaim, value)));
        claims.AddRange(grants.Select(static value => new Claim(AuthorityOptions.DefaultGrantClaim, value)));
        claims.AddRange(connections.Select(static value => new Claim(AuthorityOptions.DefaultConnectionClaim, value)));
        claims.Add(new Claim(
            AuthorityOptions.DefaultPolicyVersionClaim,
            policyVersion.ToString(CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer32));
        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims);
        }

        var token = _tokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Audience = audience ?? Audience,
            Expires = expires.UtcDateTime,
            IssuedAt = issued.UtcDateTime,
            Issuer = issuer ?? Issuer,
            NotBefore = issued.UtcDateTime,
            SigningCredentials = useUntrustedSigningKey ? UntrustedSigningCredentials : SigningCredentials,
            Subject = new ClaimsIdentity(claims),
        });

        return new AuthorityAuthenticationRequest(new AuthorityAuthenticationEvidence("Bearer", token));
    }
}
