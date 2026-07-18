using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Hosting;
using TripRadar.Server.API.Security;

namespace TripRadar.Server.API.Middlewares;

public sealed class AntiforgeryCookieMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (ShouldIssueToken(context.Request))
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            if (!string.IsNullOrWhiteSpace(tokens.RequestToken))
            {
                AuthCookieHelper.SetAntiforgeryRequestTokenCookie(
                    context.Response,
                    ResolveEnvironment(context),
                    tokens.RequestToken,
                    DateTimeOffset.UtcNow.AddHours(1));
            }
        }

        await next(context);
    }

    private static bool ShouldIssueToken(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (AuthCookieHelper.IsApiClient(request))
        {
            return false;
        }

        return AuthCookieHelper.HasAuthCookies(request);
    }

    private static IHostEnvironment ResolveEnvironment(HttpContext context) =>
        context.RequestServices.GetRequiredService<IHostEnvironment>();
}
