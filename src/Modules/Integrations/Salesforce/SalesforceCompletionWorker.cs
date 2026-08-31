using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Integrations.Salesforce;

// OAuth completion belongs to the kernel, not the browser connection's lifetime.
internal sealed class SalesforceCompletionWorker(
    SalesforceConnections connections,
    ILogger<SalesforceCompletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var failures = await connections.DeliverPendingCompletionsAsync(stoppingToken).ConfigureAwait(false);
                if (failures != 0)
                {
                    logger.LogWarning("Salesforce login completion is pending for {Count} requests; the kernel will retry them.", failures);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Do not attach the exception: remote exception payloads can contain credentials.
                logger.LogWarning("A Salesforce login completion could not be delivered; the kernel will retry it.");
            }
        }
    }
}
