using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Logs every topology warning once at host start (deprecated spellings, an unset cloud key vault).</summary>
internal sealed partial class TopologyWarningLog(TopologySettings settings, ILoggerFactory? loggers = null) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var log = (loggers ?? NullLoggerFactory.Instance).CreateLogger("DigitalBrain.Runtime.Orleans.Topology");
        foreach (var warning in settings.Warnings) { Warn(log, warning); }
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Warning}")]
    private static partial void Warn(ILogger logger, string warning);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
