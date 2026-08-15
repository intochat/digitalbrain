namespace DigitalBrain.Kernel;

internal sealed class HttpsStanceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!RequestNetwork.IsLoopback(context) && !RequestNetwork.IsSecureTransport(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(
                "HTTPS is required beyond localhost.",
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
