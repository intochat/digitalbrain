namespace DigitalBrain.Kernel.Visualization;

// Forces the TimelineRelayGrain to activate at kernel startup so its implicit
// subscription to the global timeline is wired BEFORE the first synapse fires.
// Orleans 10 will eventually activate it on the first message, but pre-warming
// avoids a race where the very first RfwCard slips past an unattached subscriber.
internal sealed class TimelineRelayActivator(
    IGrainFactory grains,
    ILogger<TimelineRelayActivator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var relay = grains.GetGrain<ITimelineRelayGrain>(Guid.Empty);
            await relay.EnsureActivatedAsync();
            logger.LogInformation("Timeline relay activated.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to pre-activate timeline relay; it will activate on first message.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
