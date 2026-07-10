using System.Collections.Concurrent;

namespace DigitalBrain.Core.V2;

public sealed record V2TraceContext(string TraceId, string SpanId, TenantId TenantId, WorkspaceId WorkspaceId, string? CommandId = null, string? OperationId = null);
public sealed record V2MetricPoint(string Name, double Value, IReadOnlyDictionary<string, string> Labels);

public interface IV2TelemetrySink
{
    ValueTask EmitAsync(V2MetricPoint point, CancellationToken cancellationToken = default);
    ValueTask EmitTraceAsync(V2TraceContext context, string eventName, string? safeDetail = null, CancellationToken cancellationToken = default);
}

public sealed class V2TelemetryBuffer(int capacity = 2048) : IV2TelemetrySink
{
    private readonly ConcurrentQueue<V2MetricPoint> _metrics = new();
    private readonly ConcurrentQueue<(V2TraceContext Context, string EventName, string? Detail)> _traces = new();
    private int _dropped;
    public int Dropped => Volatile.Read(ref _dropped);
    public IReadOnlyCollection<V2MetricPoint> Metrics => _metrics.ToArray();
    public IReadOnlyCollection<(V2TraceContext Context, string EventName, string? Detail)> Traces => _traces.ToArray();

    public ValueTask EmitAsync(V2MetricPoint point, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_metrics.Count >= capacity) { Interlocked.Increment(ref _dropped); return ValueTask.CompletedTask; }
        _metrics.Enqueue(Normalize(point));
        return ValueTask.CompletedTask;
    }

    public ValueTask EmitTraceAsync(V2TraceContext context, string eventName, string? safeDetail = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Traces are telemetry too: keep the queue bounded so a collector outage cannot
        // turn an unbounded diagnostic stream into process-wide memory pressure.
        if (_traces.Count >= capacity)
        {
            Interlocked.Increment(ref _dropped);
            return ValueTask.CompletedTask;
        }

        _traces.Enqueue((context, eventName, V2Redaction.SafeSummary(safeDetail)));
        return ValueTask.CompletedTask;
    }

    private static V2MetricPoint Normalize(V2MetricPoint point)
    {
        var labels = point.Labels.Where(x => x.Key is "profile" or "outcome" or "provider" or "capability" or "status")
            .ToDictionary(x => x.Key, x => V2Redaction.SafeSummary(x.Value), StringComparer.Ordinal);
        return point with { Labels = labels };
    }
}
