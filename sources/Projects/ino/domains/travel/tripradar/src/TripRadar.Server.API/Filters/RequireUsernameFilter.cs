using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Security;

namespace TripRadar.Server.API.Filters;

internal sealed class RequireUsernameFilter(ICurrentRequestUserProvider currentRequestUserProvider) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (EndpointMetadataMatcher.AllowsAnonymous(context.ActionDescriptor.EndpointMetadata))
            return;

        if (currentRequestUserProvider.TryGetUsername(out _))
            return;

        context.Result = new UnauthorizedResult();
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class RequireUsernameAttribute() : TypeFilterAttribute(typeof(RequireUsernameFilter));
