using System.Security.Claims;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Runtime.Http;

/// <summary>
/// Builds the claims principal an <see cref="ISessionIssuer"/> writes into its cookie, and reads the member back. The
/// identity-owned claims (<see cref="SessionClaims.IdentityOwned"/>) come from the member only: a request claim with one of
/// those names is rejected (<see cref="SessionClaims.Validate"/>), never merged.
/// </summary>
public static class SessionPrincipal
{
    /// <summary>The claim carrying the session id.</summary>
    public const string SessionIdClaim = "sid";

    /// <summary>The principal for <paramref name="issued"/>: the member claims, the session id and the request's extra claims.</summary>
    /// <exception cref="ArgumentException">The request names an identity-owned claim.</exception>
    public static ClaimsPrincipal Create(SessionRequest request, SessionIssued issued, string authenticationType)
    {
        SessionClaims.Validate(request);
        ArgumentNullException.ThrowIfNull(issued);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationType);
        var claims = SessionClaims.Of(request.Member).Select(pair => new Claim(pair.Key, pair.Value)).ToList();
        claims.Add(new Claim(SessionIdClaim, issued.SessionId));
        claims.AddRange(request.Claims
            .Where(pair => !string.Equals(pair.Key, SessionIdClaim, StringComparison.Ordinal))
            .Select(pair => new Claim(pair.Key, pair.Value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType, SessionClaims.Principal, SessionClaims.Role));
    }

    /// <summary>The principal id of an authenticated <paramref name="user"/>, or null.</summary>
    public static string? PrincipalId(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true && user.FindFirst(SessionClaims.Principal)?.Value is { Length: > 0 } principal ? principal : null;

    /// <summary>The session id of <paramref name="user"/>, or null.</summary>
    public static string? SessionId(ClaimsPrincipal? user) => user?.FindFirst(SessionIdClaim)?.Value;
}
