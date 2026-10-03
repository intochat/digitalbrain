using System.Globalization;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Data;
using DigitalBrain.Kernel;
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
        next.SourceNeuronId = null;
        next.Query = null;
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

        next.SourceNeuronId = null;
        next.Query = null;
        return Save(next, new ChartChanged(this.GetPrimaryKeyString(), next.Version, next.Title));
    }

    public Task Bind(string sourceNeuronId, RowQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNeuronId);
        ArgumentNullException.ThrowIfNull(query);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.SourceNeuronId = sourceNeuronId;
        next.Query = query.Degrade(new SourceCapabilities());
        next.Points = [];
        return Save(next, new ChartChanged(this.GetPrimaryKeyString(), next.Version, next.Title));
    }

    [ReadOnly]
    public async Task<ChartState> Read()
    {
        var state = Snapshot;
        if (string.IsNullOrEmpty(state.SourceNeuronId))
        {
            state.Name = this.GetPrimaryKeyString();
            return state;
        }

        var source = RowSourceAddress.Open(GrainFactory, state.SourceNeuronId);
        var capabilities = await source.ReadCapabilities();
        var page = await source.Read((state.Query ?? new RowQuery()).Degrade(capabilities));
        return new ChartState
        {
            Name = this.GetPrimaryKeyString(),
            Version = state.Version,
            Title = state.Title,
            Kind = state.Kind,
            Points = [.. page.Rows.Take(UIVocabulary.ChartMaxPoints).Select((row, index) => new ChartPoint(
                index.ToString(CultureInfo.InvariantCulture),
                row.Values.Length > 0 ? row.Values[0] : "",
                row.Values.Length > 1 && double.TryParse(row.Values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0))],
            SourceNeuronId = state.SourceNeuronId,
            Query = state.Query,
        };
    }
}
