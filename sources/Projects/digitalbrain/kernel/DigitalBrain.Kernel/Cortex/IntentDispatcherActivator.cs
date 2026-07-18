namespace DigitalBrain.Kernel.Cortex;

// Pre-activates IntentDispatcher at kernel startup so its implicit subscription
// to the global timeline is wired before the first IntentClassified fires.
internal sealed class IntentDispatcherActivator(
    IGrainFactory grains,
    ILogger<IntentDispatcherActivator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dispatcher = grains.GetGrain<IIntentDispatcher>(Guid.Empty);
            await dispatcher.EnsureActivatedAsync();
            logger.LogInformation("Intent dispatcher activated.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to pre-activate IntentDispatcher; it will activate on first message.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
