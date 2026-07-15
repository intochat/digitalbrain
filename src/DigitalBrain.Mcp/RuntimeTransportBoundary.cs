using Microsoft.AspNetCore.Http.Features;
namespace DigitalBrain.Mcp;

public sealed record RuntimeTransportBoundaryOptions(int MaximumBodyBytes, int MaximumConcurrentRequests, int RequestsPerMinute, TimeSpan RequestTimeout)
{
    public static RuntimeTransportBoundaryOptions FromConfiguration(IConfiguration configuration) => new(
        ReadPositive(configuration, "DigitalBrain:Runtime:Transport:MaxBodyBytes", 2 * 1024 * 1024),
        ReadPositive(configuration, "DigitalBrain:Runtime:Transport:MaxConcurrentRequests", 32),
        ReadPositive(configuration, "DigitalBrain:Runtime:Transport:RequestsPerMinute", 600),
        TimeSpan.TryParse(configuration["DigitalBrain:Runtime:Transport:RequestTimeout"], out var timeout) && timeout > TimeSpan.Zero && timeout <= TimeSpan.FromMinutes(5)
            ? timeout
            : TimeSpan.FromMinutes(2));
    private static int ReadPositive(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) && value > 0 ? value : fallback;
}
public sealed class RuntimeTransportBoundary(RequestDelegate next, RuntimeTransportBoundaryOptions options, TimeProvider timeProvider, ILogger<RuntimeTransportBoundary> logger)
{
    private readonly SemaphoreSlim _concurrency = new(options.MaximumConcurrentRequests, options.MaximumConcurrentRequests);
    private readonly object _rateGate = new();
    private DateTimeOffset _windowStartedAt = timeProvider.GetUtcNow();
    private int _windowCount;
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsRuntimePath(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }
        if (!IsUiGrpcPath(context.Request.Path))
        {
            var bodyFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodyFeature is { IsReadOnly: false }) bodyFeature.MaxRequestBodySize = options.MaximumBodyBytes;
            if (context.Request.ContentLength is < 0 || context.Request.ContentLength > options.MaximumBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }
        }
        if (!TryTakeRateSlot() || !await _concurrency.WaitAsync(0, context.RequestAborted).ConfigureAwait(false))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        using var timeout = IsLongLivedFeed(context.Request.Path) ? null : CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        if (timeout is not null)
        {
            timeout.CancelAfter(options.RequestTimeout);
            context.RequestAborted = timeout.Token;
        }
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout?.IsCancellationRequested == true)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            }
        }
        catch (Exception exception)
        {
            logger.LogError("Runtime transport request failed with {ExceptionType}.", exception.GetType().Name);
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
            if (_windowCount >= options.RequestsPerMinute) return false;
            _windowCount++;
            return true;
        }
    }
    private static bool IsRuntimePath(PathString path) =>
        path.StartsWithSegments("/mcp") || path.StartsWithSegments("/digitalbrain.v2.ui.DigitalBrainV2Ui") ||
        path.StartsWithSegments("/oauth/start");
    private static bool IsUiGrpcPath(PathString path) =>
        path.StartsWithSegments("/digitalbrain.v2.ui.DigitalBrainV2Ui");
    private static bool IsLongLivedFeed(PathString path) =>
        path.Value?.EndsWith("/WatchSurfaceFeed", StringComparison.Ordinal) == true;
}
