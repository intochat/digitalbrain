using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Map.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Map;
[GrainType(UIVocabulary.MapType)]
internal sealed class MapNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<MapState> store)
    : Neuron<MapState>(store), IMap
{
    public Task Set(double lat, double lng, double zoom, IReadOnlyList<MapMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        if (zoom <= 0) { throw new ArgumentOutOfRangeException(nameof(zoom)); }
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Lat = lat;
        next.Lng = lng;
        next.Zoom = zoom;
        next.Markers = [.. markers];
        return Save(next, new MapChanged(this.GetPrimaryKeyString(), next.Version));
    }

    [ReadOnly] public Task<MapState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}

