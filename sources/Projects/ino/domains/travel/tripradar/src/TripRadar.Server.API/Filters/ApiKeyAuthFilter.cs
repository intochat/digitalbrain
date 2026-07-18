using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Security;

namespace TripRadar.Server.API.Filters;

internal class ApiKeyAuthFilter(IApiKeyValidator apiKeyValidator) : IAuthorizationFilter
{
    private const string ApiKeyHeaderName = "X-API-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var hasInternalAttribute = EndpointMetadataMatcher.IsInternal(context.ActionDescriptor.EndpointMetadata);
        if (hasInternalAttribute)
            return;

        if (EndpointMetadataMatcher.AllowsAnonymous(context.ActionDescriptor.EndpointMetadata))
            return;

        if (context.HttpContext.Request.Headers.Authorization.Any(h =>
                h is not null && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)))
            return;

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey) ||
            extractedApiKey.Count != 1 ||
            string.IsNullOrWhiteSpace(extractedApiKey[0]))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (apiKeyValidator.IsValid(extractedApiKey[0]))
            return;

        context.Result = new UnauthorizedResult();
    }
}
