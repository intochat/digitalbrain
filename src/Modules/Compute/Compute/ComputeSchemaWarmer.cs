using DigitalBrain.Compute.Metering;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Compute;

// Creates the meter table and warms the pool at startup, so the first meter batch of a turn
// pays only its INSERT, never the schema DDL or a cold connection.
internal sealed class ComputeSchemaWarmer(IMeterStore store, ILogger<ComputeSchemaWarmer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await store.EnsureCreatedAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // a warmup failure must not stop the silo; the append path retries lazily
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Compute meter schema warmup failed; the first append will create it.");
        }
#pragma warning restore CA1031
    }
}
