using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;
using TripRadar.Server.API.Security;

namespace TripRadar.Server.API.Filters;

public sealed class CookieAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) ||
            HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) ||
            HttpMethods.IsTrace(request.Method))
        {
            return;
        }

        if (context.Filters.OfType<IAllowAnonymousFilter>().Any() ||
            context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return;
        }

        if (AuthCookieHelper.IsApiClient(request))
        {
            return;
        }

        if (!AuthCookieHelper.HasAuthCookies(request))
        {
            return;
        }

        if (request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationHeader) &&
            authorizationHeader.Any(value => value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await antiforgery.ValidateRequestAsync(context.HttpContext);
    }
}
