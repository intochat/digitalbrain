using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.Microsoft.Aspire;

namespace DigitalBrain.SDK.Microsoft.Aspire.Runtime;

public sealed class AspireAppStartedEmitter(
    IServiceProvider serviceProvider,
    ILogger<AspireAppStartedEmitter> logger) : IHostedService
{
    int _fired;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _fired, 1) != 0)
            return;

        var signalEmitter = serviceProvider.GetService<ISynapseEmitter>();
        if (signalEmitter == null)
        {
            logger.LogInformation("ISynapseEmitter is not registered on this silo; skipping SDK.Aspire ingress signal emission.");
            return;
        }

        try
        {
            var payload = (IReadOnlyDictionary<string, string>)
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["profile"] = "default",
                };
            await signalEmitter.EmitAsync(
                AppStartedSignal.Identity, payload, cancellationToken);

            logger.LogInformation(
                "Emitted SDK.Aspire ingress signal {Identity}.", AppStartedSignal.Identity);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to emit SDK.Aspire ingress signal {Identity}.",
                AppStartedSignal.Identity);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
