using DigitalBrain.Core.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class RuntimeRequestAuthenticator(
    RuntimeSessionAuthority sessions,
    UiExternalIdentityAuthenticator externalIdentity)
{
    private const int MaximumAuthorizationHeaderLength = 8 * 1024;

    public async Task<RuntimeRequestContext?> AuthenticateMcpAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var authorization = httpContext.Request.Headers.Authorization;
        if (authorization.Count != 1 || authorization[0] is not { } header ||
            header.Length is 0 or > MaximumAuthorizationHeaderLength ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = header["Bearer ".Length..];
        if (string.IsNullOrWhiteSpace(token) || !string.Equals(token, token.Trim(), StringComparison.Ordinal))
            return null;

        var durable = await sessions.ValidateAccessAsync(
            token,
            SessionAudiences.Mcp,
            cancellationToken).ConfigureAwait(false);
        if (durable is not null) return durable.Context;

        var external = await externalIdentity.AuthenticateAsync(httpContext, cancellationToken).ConfigureAwait(false);
        return external.Status == UiExternalAuthenticationStatus.Authenticated
            ? external.Context
            : null;
    }
}
