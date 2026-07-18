namespace TripRadar.Server.API.Middlewares;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
        headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");

        headers.TryAdd(
            "Permissions-Policy",
            "geolocation=(), microphone=(), camera=()"
        );

        await next(context);
    }
}
