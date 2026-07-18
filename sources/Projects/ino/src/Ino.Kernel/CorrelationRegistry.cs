using Ino.Core.Hosting.Placement;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;

namespace Ino.Kernel;

/// <summary>
/// Kernel-pinned grain that maps a chat correlation_id back to the plan
/// grain that authored an outbound RFW payload. The gateway records the
/// mapping when it stamps an RFW response and looks it up when the client
/// fires an RfwEvent so it can dispatch the callback to the originating
/// plan via <see cref="IRfwEventHandler.HandleRfwEventAsync"/>.
///
/// In-memory and volatile — silo restart drops in-flight trip correlations,
/// matching the v0.1 scope. Persistence is tracked under issue #22.
/// </summary>
[PinToSilo("kernel")]
public sealed class CorrelationRegistry(ILogger<CorrelationRegistry>? logger = null)
    : Grain, ICorrelationRegistry
{
    private readonly Dictionary<string, CorrelationEntry> _map = new(StringComparer.Ordinal);
    private readonly ILogger _log = (ILogger?)logger ?? NullLogger.Instance;

    public Task RegisterAsync(string correlationId, string planInterfaceAqn, string grainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(planInterfaceAqn);
        ArgumentException.ThrowIfNullOrWhiteSpace(grainKey);

        _map[correlationId] = new CorrelationEntry(planInterfaceAqn, grainKey);
        _log.LogDebug("rfw correlation registered: {CorrelationId} -> {PlanType}({GrainKey})",
            correlationId, planInterfaceAqn, grainKey);
        return Task.CompletedTask;
    }

    public Task<CorrelationEntry?> GetAsync(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return Task.FromResult<CorrelationEntry?>(null);

        return Task.FromResult(_map.TryGetValue(correlationId, out var entry) ? entry : null);
    }
}
