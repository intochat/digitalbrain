using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Ui;

namespace DigitalBrain.Kernel.Visualization;

// Pure projection logic shared between the grain (TaskManagerNeuron) and its
// xUnit tests. Keeping it stateless and static lets the test class exercise
// the real LRU + sweep + projection math without booting Orleans.
internal static class TaskManagerProjection
{
    public static void Observe(
        Dictionary<Guid, ActiveTask> active,
        LinkedList<Guid> lru,
        int maxTracked,
        Synapse synapse,
        Action<ActiveTask> evictedCallback)
    {
        var correlationId = synapse.CorrelationId;
        if (!active.TryGetValue(correlationId, out var task))
        {
            if (active.Count >= maxTracked)
            {
                var oldest = lru.First;
                if (oldest is not null && active.TryGetValue(oldest.Value, out var evicted))
                {
                    active.Remove(oldest.Value);
                    lru.RemoveFirst();
                    evictedCallback(evicted);
                }
            }
            task = new ActiveTask(
                correlationId,
                synapse.CallerNeuronType ?? "external",
                synapse.Timestamp);
            active[correlationId] = task;
            lru.AddLast(correlationId);
        }
        else
        {
            // Refresh LRU position so genuinely-newer activity outlives stale rows.
            lru.Remove(correlationId);
            lru.AddLast(correlationId);
        }

        task.LastSeenAt = synapse.Timestamp;
        task.EdgeCount++;
        if (!string.IsNullOrEmpty(synapse.ReceiverNeuronType)
            && !task.Participating.Contains(synapse.ReceiverNeuronType))
        {
            task.Participating.Add(synapse.ReceiverNeuronType);
        }
    }

    public static void Sweep(
        Dictionary<Guid, ActiveTask> active,
        LinkedList<Guid> lru,
        TimeSpan idleTimeout,
        DateTimeOffset now,
        Action<ActiveTask> agedOutCallback)
    {
        var cutoff = now - idleTimeout;
        foreach (var (correlationId, task) in active.ToArray())
        {
            if (task.LastSeenAt < cutoff)
            {
                active.Remove(correlationId);
                lru.Remove(correlationId);
                agedOutCallback(task);
            }
        }
    }

    public static TaskManagerCardPayload Project(
        IEnumerable<ActiveTask> active,
        int completed,
        int failed,
        DateTimeOffset now)
    {
        var rows = active
            .Select(task =>
            {
                var correlationText = task.CorrelationId.ToString();
                return new TaskManagerRow(
                    CorrelationId: correlationText,
                    ShortHash:     correlationText[..8],
                    OriginNeuron:  task.OriginNeuronType,
                    OriginIcon:    task.OriginNeuronType.Split('.', '/').Last(),
                    AgeMs:         (long)(now - task.FirstSeenAt).TotalMilliseconds,
                    EdgeCount:     task.EdgeCount,
                    Status:        task.Status,
                    Participating: task.Participating.ToArray()
                );
            })
            .ToArray();
        return new TaskManagerCardPayload(
            Tasks: rows,
            Totals: new TaskManagerTotals(rows.Length, completed, failed));
    }

    // Hash over activity-derived fields only — excludes AgeMs which advances every
    // tick and would defeat the no-delta optimization in production.
    public static string Signature(TaskManagerCardPayload payload)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var row in payload.Tasks)
        {
            sb.Append(row.CorrelationId).Append('|')
              .Append(row.OriginNeuron).Append('|')
              .Append(row.EdgeCount).Append('|')
              .Append(row.Status).Append('|')
              .Append(string.Join(',', row.Participating)).Append(';');
        }
        sb.Append('=')
          .Append(payload.Totals.Active).Append(',')
          .Append(payload.Totals.Completed).Append(',')
          .Append(payload.Totals.Failed);
        return sb.ToString();
    }
}

internal sealed class ActiveTask(Guid correlationId, string originNeuronType, DateTimeOffset firstSeenAt)
{
    public Guid CorrelationId { get; } = correlationId;
    public string OriginNeuronType { get; } = originNeuronType;
    public DateTimeOffset FirstSeenAt { get; } = firstSeenAt;
    public DateTimeOffset LastSeenAt { get; set; } = firstSeenAt;
    public int EdgeCount { get; set; }
    public string Status { get; set; } = "running";
    public List<string> Participating { get; } = [];
}
