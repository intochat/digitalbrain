using TripRadar.Server.API.Contracts;
using TripRadar.Server.Comms.Core.Extensions;

namespace TripRadar.Server.API.Security;

public class CurrentRequestUserProvider(
    IHttpContextAccessor httpContextAccessor,
    ILogger<CurrentRequestUserProvider> logger) : ICurrentRequestUserProvider
{
    public bool TryGetUserId(out long userId)
    {
        userId = 0;
        return httpContextAccessor.HttpContext?.User.TryGetUserId(out userId) == true;
    }

    public bool TryGetUsername(out string username)
    {
        var httpContext = httpContextAccessor.HttpContext;
        username = httpContext?.User.GetUsername() ?? string.Empty;
        var hasUsername = !string.IsNullOrWhiteSpace(username);

        if (httpContext?.Request.Path.StartsWithSegments("/graphql") != true)
            return hasUsername;

        if (hasUsername)
            logger.LogDebug(
                "Resolved GraphQL username {Username}. TraceId={TraceId} AuthType={AuthType}",
                username,
                httpContext.TraceIdentifier,
                httpContext.User.Identity?.AuthenticationType);
        else
            logger.LogWarning(
                "GraphQL request is missing authenticated username. TraceId={TraceId} AuthType={AuthType} IsAuthenticated={IsAuthenticated}",
                httpContext.TraceIdentifier,
                httpContext.User.Identity?.AuthenticationType,
                httpContext.User.Identity?.IsAuthenticated ?? false);

        return hasUsername;
    }
}
