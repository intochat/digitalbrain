namespace DigitalBrain.Kernel;

// The net10+ AG-UI adapter lets streaming exceptions escape. End the protocol cleanly
// so Flutter can stop its progress indicator without exposing provider error bodies.
internal sealed class AgentStreamResult : IResult
{
    private readonly IResult? _response;
    private readonly Exception? _error;

    public AgentStreamResult(IResult response) => _response = response;
    public AgentStreamResult(Exception error) => _error = error;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        try
        {
            if (_error is { } error)
            {
                await WriteFailureAsync(httpContext, error);
            }
            else
            {
                await _response!.ExecuteAsync(httpContext);
            }
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The user stopped or disconnected; there is no client to notify.
        }
        catch (Exception error) when (!httpContext.RequestAborted.IsCancellationRequested)
        {
            await WriteFailureAsync(httpContext, error);
        }
    }

    private static async Task WriteFailureAsync(HttpContext context, Exception error)
    {
        context.RequestServices.GetRequiredService<ILogger<AgentStreamResult>>()
            .LogError(error, "Conversational agent request failed");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
        }
        await context.Response.WriteAsync("data: {\"type\":\"RUN_ERROR\",\"code\":\"agent_failure\",\"message\":\"The assistant could not finish this response. Please try again.\"}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
}
