using System.Collections.Concurrent;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Core.Runtime;

public sealed record TraceContext(string TraceId, string SpanId, BrainOwnerId OwnerId, ActorId ActorId, string? CommandId = null, string? OperationId = null);
public sealed record MetricPoint(string Name, double Value, IReadOnlyDictionary<string, string> Labels);

public interface ITelemetrySink
{
    ValueTask EmitAsync(MetricPoint point, CancellationToken cancellationToken = default);
    ValueTask EmitTraceAsync(TraceContext context, string eventName, string? safeDetail = null, CancellationToken cancellationToken = default);
}

public sealed class TelemetryBuffer(int capacity = 2048) : ITelemetrySink
{
    private readonly ConcurrentQueue<MetricPoint> _metrics = new();
    private readonly ConcurrentQueue<(TraceContext Context, string EventName, string? Detail)> _traces = new();
    private int _dropped;
    public int Dropped => Volatile.Read(ref _dropped);
    public IReadOnlyCollection<MetricPoint> Metrics => _metrics.ToArray();
    public IReadOnlyCollection<(TraceContext Context, string EventName, string? Detail)> Traces => _traces.ToArray();

    public ValueTask EmitAsync(MetricPoint point, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_metrics.Count >= capacity) { Interlocked.Increment(ref _dropped); return ValueTask.CompletedTask; }
        _metrics.Enqueue(Normalize(point));
        return ValueTask.CompletedTask;
    }

    public ValueTask EmitTraceAsync(TraceContext context, string eventName, string? safeDetail = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Traces are telemetry too: keep the queue bounded so a collector outage cannot
        // turn an unbounded diagnostic stream into process-wide memory pressure.
        if (_traces.Count >= capacity)
        {
            Interlocked.Increment(ref _dropped);
            return ValueTask.CompletedTask;
        }

        _traces.Enqueue((context, eventName, Redaction.SafeSummary(safeDetail)));
        return ValueTask.CompletedTask;
    }

    private static MetricPoint Normalize(MetricPoint point)
    {
        var labels = point.Labels.Where(x => x.Key is "profile" or "outcome" or "provider" or "capability" or "status")
            .ToDictionary(x => x.Key, x => Redaction.SafeSummary(x.Value), StringComparer.Ordinal);
        return point with { Labels = labels };
    }
}
