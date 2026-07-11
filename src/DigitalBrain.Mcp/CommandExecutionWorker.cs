using DigitalBrain.Core.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Mcp;

/// <summary>Durable latency-hint loop; the operation journal remains the source of truth.</summary>
public sealed class CommandExecutionWorker(
    ApplicationService application,
    CommandDispatcher dispatcher,
    ILogger<CommandExecutionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var operationId in application.GetPendingOperationIds())
            {
                try { await dispatcher.DispatchAsync(operationId, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogError(ex, "Command dispatch failed."); }
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
