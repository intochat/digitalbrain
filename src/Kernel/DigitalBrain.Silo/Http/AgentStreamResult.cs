namespace DigitalBrain.Kernel;

// The net10+ AG-UI adapter lets streaming exceptions escape. End the protocol cleanly
// so Flutter can stop its progress indicator without exposing provider error bodies.
internal sealed class AgentStreamResult : IResult
{
    private readonly IResult? _response;
    private readonly Exception? _error;
    private readonly string? _runId;

    public AgentStreamResult(IResult response, string? runId = null)
    {
        _response = response;
        _runId = runId;
    }

    public AgentStreamResult(Exception error) => _error = error;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        try
        {
            if (_error is { } error)
            {
                await WriteFailureAsync(httpContext, error);
                return;
            }

            if (_runId is null)
            {
                await _response!.ExecuteAsync(httpContext);
                return;
            }

            // Watch the frames on their way out, so a reconnect for this run id can be answered from what
            // the model already produced.
            var tap = new AgentAnswerTap(httpContext.Response.Body,
                httpContext.RequestServices.GetRequiredService<AgentRunLedger>(), _runId);
            var body = httpContext.Response.Body;
            httpContext.Response.Body = tap;
            try
            {
                await _response!.ExecuteAsync(httpContext);
            }
            finally
            {
                httpContext.Response.Body = body;
                tap.Complete();
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
