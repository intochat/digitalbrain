using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Identity;

public static class IdentityAuthentication
{
    public const string Scheme = "DigitalBrainSession";
    public const string CookieName = "__Host-DigitalBrainSession";
    public const string CsrfHeader = "X-CSRF-Token";
    public const string SessionToken = "DigitalBrain.Identity.SessionToken";
    public const string AuthenticatedIdentity = "DigitalBrain.Identity.AuthenticatedIdentity";

    public static bool HasSessionCredential(HttpRequest request)
        => request.Headers.Authorization.ToString().StartsWith("Bearer", StringComparison.OrdinalIgnoreCase)
            || request.Cookies.ContainsKey(CookieName);
}

public sealed class IdentityAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IdentityService identities) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        var bearer = authorization.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase);
        var token = bearer
            ? (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..].Trim() : "")
            : Request.Cookies[IdentityAuthentication.CookieName];
        if (token is null) { return AuthenticateResult.NoResult(); }
        if (token.Length is 0 or > 512) { return AuthenticateResult.Fail("Invalid session."); }
        var identity = await identities.AuthenticateAsync(token).ConfigureAwait(false);
        if (identity is null) { return AuthenticateResult.Fail("Invalid session."); }
        if (!bearer && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method)
            && !HttpMethods.IsOptions(Request.Method)
            && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(identity.CsrfToken),
                Encoding.UTF8.GetBytes(Request.Headers[IdentityAuthentication.CsrfHeader].ToString())))
        {
            return AuthenticateResult.Fail("Missing or invalid CSRF token.");
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identity.UserId) };
        if (identity.IsOwner) { claims.Add(new(ClaimTypes.Role, "owner")); }
        Context.Items[IdentityAuthentication.SessionToken] = token;
        Context.Items[IdentityAuthentication.AuthenticatedIdentity] = identity;
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityAuthentication.Scheme)), IdentityAuthentication.Scheme));
    }
}
