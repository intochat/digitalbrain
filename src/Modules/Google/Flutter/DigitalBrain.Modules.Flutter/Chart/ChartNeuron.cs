using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chart.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Chart;

[GrainType(UIVocabulary.ChartType)]
internal sealed class ChartNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ChartState> store)
    : Neuron<ChartState>(store), IChart
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase) { "bar", "line", "pie" };

    public Task Render(string title, string kind, IReadOnlyList<ChartPoint> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(points);
        if (!Kinds.Contains(kind)) { throw new ArgumentOutOfRangeException(nameof(kind)); }
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Title = title.Trim();
        next.Kind = kind.Trim().ToLowerInvariant();
        next.Points = [.. points.TakeLast(UIVocabulary.ChartMaxPoints)];
        return Save(next, new ChartChanged(this.GetPrimaryKeyString(), next.Version, next.Title));
    }

    public Task Append(ChartPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentException.ThrowIfNullOrWhiteSpace(point.Label);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        if (!string.IsNullOrWhiteSpace(point.EventId) && next.Points.Exists(existing => existing.EventId == point.EventId))
        {
            return Task.CompletedTask;
        }

        next.Version++;
        next.Points.Add(point);
        if (next.Points.Count > UIVocabulary.ChartMaxPoints)
        {
            next.Points.RemoveRange(0, next.Points.Count - UIVocabulary.ChartMaxPoints);
        }

        return Save(next, new ChartChanged(this.GetPrimaryKeyString(), next.Version, next.Title));
    }

    [ReadOnly] public Task<ChartState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}