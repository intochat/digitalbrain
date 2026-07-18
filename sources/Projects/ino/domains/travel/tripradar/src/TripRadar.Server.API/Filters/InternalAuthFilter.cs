using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Security;

namespace TripRadar.Server.API.Filters;

internal class InternalAuthFilter(IInternalAccessValidator internalAccessValidator) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var hasInternalAttribute = EndpointMetadataMatcher.IsInternal(context.ActionDescriptor.EndpointMetadata);

        if (!hasInternalAttribute) return;

        var validationResult = internalAccessValidator.Validate(context.HttpContext);
        if (validationResult.IsAuthorized) return;

        context.Result = validationResult.IsMissingApiKey ? new UnauthorizedResult() : new ForbidResult();
    }
}
