using DigitalBrain.Runtime.Orleans.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

internal sealed partial class SubscriptionSweep(
    IServiceProvider services,
    IOptions<RetentionOptions> options,
    TimeProvider time,
    ILogger<SubscriptionSweep>? logger = null) : ILifecycleParticipant<ISiloLifecycle>, IDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<SubscriptionSweep>.Instance;
    private ITimer? _timer;

    public void Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(nameof(SubscriptionSweep), ServiceLifecycleStage.Active,
            _ =>
            {
                var period = options.Value.ReconcileEvery;
                if (period > TimeSpan.Zero)
                {
                    _timer = time.CreateTimer(_ => Sweep(), null, period, period);
                }
                return Task.CompletedTask;
            },
            _ =>
            {
                _timer?.Dispose();
                _timer = null;
                return Task.CompletedTask;
            });
    }

    public void Sweep()
    {
        var logs = services.GetService<BrainLogs>();
        if (logs is null) { return; }
        foreach (var log in logs.Opened)
        {
            try
            {
                log.Fanout?.Reconcile();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogSweepFailed(_logger, ex, log.BrainId);
            }
        }
    }

    [LoggerMessage(EventId = 4135, Level = LogLevel.Warning, Message = "Subscription reconciliation sweep failed for brain {BrainId}")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex, string brainId);

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
