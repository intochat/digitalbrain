using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Discovery;

// Warm the catalog at startup; failures leave keyword search available and never stop the host.
internal sealed class CapabilityCatalogRebuilder(CapabilityCatalog catalog, ILogger<CapabilityCatalogRebuilder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
