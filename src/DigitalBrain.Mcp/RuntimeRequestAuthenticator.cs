using DigitalBrain.Kernel.Contracts.Runtime;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
namespace DigitalBrain.Mcp;

public sealed class RuntimeRequestAuthenticator(
    RuntimeSessionAuthority sessions,
    UiExternalIdentityAuthenticator externalIdentity,
    UiDevelopmentLoginAuthenticator developmentLogin,
    UiDevelopmentLoginOptions developmentLoginOptions)
{
    private const int MaximumAuthorizationHeaderLength = 8 * 1024;
    public async Task<RuntimeRequestContext?> AuthenticateMcpAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var authorization = httpContext.Request.Headers.Authorization;
        if (authorization.Count == 1 && authorization[0] is { } header && header.Length is > 0 and <= MaximumAuthorizationHeaderLength &&
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..];
            if (!string.IsNullOrWhiteSpace(token) && string.Equals(token, token.Trim(), StringComparison.Ordinal))
            {
                var durable = await sessions.ValidateAccessAsync(token, SessionAudiences.Mcp, cancellationToken).ConfigureAwait(false);
                if (durable is not null) return durable.Context;
                var external = await externalIdentity.AuthenticateAsync(httpContext, cancellationToken).ConfigureAwait(false);
                if (external.Status == UiExternalAuthenticationStatus.Authenticated) return external.Context;
            }
        }
        if (!developmentLoginOptions.Enabled) return null;
        if (!developmentLogin.TryAuthenticate(developmentLoginOptions.Username, developmentLoginOptions.Password, out var context))
            return null;
        return context with
        {
            Grants = context.Grants.Append("brain.interact").ToHashSet(StringComparer.Ordinal)
        };
    }
}
