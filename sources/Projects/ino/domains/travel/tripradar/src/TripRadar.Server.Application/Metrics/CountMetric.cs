using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using TripRadar.Server.Application.Constants;

namespace TripRadar.Server.Application.Metrics;

public class CountMetric
{
    public static readonly ActivitySource ActivitySource = new(MetricConstants.ApplicationName);
    private readonly ConcurrentDictionary<string, Counter<int>> _counters = new();
    private readonly Meter _meter = new(MetricConstants.ApplicationName, "1.0.0");

    public void UpdateMetric(string name, int value, IDictionary<string, object?>? tags = null)
    {
        var counter = _counters.GetOrAdd(name,
            _ => _meter.CreateCounter<int>(name, "count", MetricConstants.GetDescription(name)));
        counter.Add(value, tags?.ToArray() ?? ReadOnlySpan<KeyValuePair<string, object?>>.Empty);
    }

    public static IDictionary<string, object?> SetResult(bool success)
    {
        return new Dictionary<string, object?> { ["success"] = success };
    }
}
