namespace DigitalBrain.Kernel.Hosting;

public sealed class OAuthTransportBoundary(
    RequestDelegate next,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<OAuthTransportBoundary> logger)
{
    private const int RequestsPerMinute = 120;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _concurrency = new(16, 16);
    private readonly object _rateGate = new();
    private DateTimeOffset _windowStartedAt = timeProvider.GetUtcNow();
    private int _windowCount;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/oauth"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        if (!HttpMethods.IsGet(context.Request.Method) ||
            context.Request.ContentLength is < 0 or > 0)
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        if (environment.IsProduction() && !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }
        if (!TryTakeRateSlot() || !await _concurrency.WaitAsync(0, context.RequestAborted).ConfigureAwait(false))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(RequestTimeout);
        context.RequestAborted = timeout.Token;
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                "OAuth transport request failed with {ExceptionType}.",
                exception.GetType().Name);
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }
            context.Abort();
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private bool TryTakeRateSlot()
    {
        lock (_rateGate)
        {
            var now = timeProvider.GetUtcNow();
            if (now - _windowStartedAt >= TimeSpan.FromMinutes(1))
            {
                _windowStartedAt = now;
                _windowCount = 0;
            }
            if (_windowCount >= RequestsPerMinute) return false;
            _windowCount++;
            return true;
        }
    }
}
