namespace TripRadar.Server.API.Middlewares;

public sealed class ContentSecurityPolicyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/scalar"))
        {
            context.Response.Headers.TryAdd(
                "Content-Security-Policy",
                string.Join("; ", new[]
                {
                    "default-src 'none'",
                    "base-uri 'none'",
                    "form-action 'none'",
                    "frame-ancestors 'self'",
                    "connect-src 'self'",
                    "img-src 'self'",
                    "style-src 'self'",
                    "script-src 'self'"
                })
            );
        }

        await next(context);
    }
}
