namespace TripRadar.Server.API.Middlewares;

public sealed class RateLimitHeaderSanitizerMiddleware(RequestDelegate next)
{
    private const string ClientIdHeader = "X-ClientId";

    public Task InvokeAsync(HttpContext context)
    {
        context.Request.Headers.Remove(ClientIdHeader);
        return next(context);
    }
}
