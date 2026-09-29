using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Registry;

// Warm the catalog at startup, then rebuild only when a source reports a change.
// A failed warmup or a lost change feed keeps search available and never stops the host.
internal sealed class CapabilityCatalogRebuilder(
    CapabilityCatalog catalog,
    IEnumerable<ICapabilitySource> sources,
    ILogger<CapabilityCatalogRebuilder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmAsync(stoppingToken).ConfigureAwait(false);
        await Task.WhenAll(sources.Select(source => WatchAsync(source, stoppingToken))).ConfigureAwait(false);
    }

    private async Task WatchAsync(ICapabilitySource source, CancellationToken stoppingToken)
    {
        try
        {
            await source.Watch(catalog.Invalidate, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // a lost change feed must not stop the silo; the index rebuilds on the next invalidation
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Change feed of {Source} stopped; its capabilities refresh on the next rebuild.", source.GetType().Name);
        }
#pragma warning restore CA1031
    }

    private async Task WarmAsync(CancellationToken stoppingToken)
    {
        try
        {
            await catalog.RebuildAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // a failed warmup must not stop the silo; search retries lazily
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Initial capability catalog rebuild failed; discovery will retry on first search.");
        }
#pragma warning restore CA1031
    }
}