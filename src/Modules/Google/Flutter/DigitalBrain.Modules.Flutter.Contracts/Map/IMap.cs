using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Map;

[Alias("map"), Orleans.Metadata.DefaultGrainType(UIVocabulary.MapType)]
public interface IMap : INeuron
{
    Task Set(double lat, double lng, double zoom, IReadOnlyList<MapMarker> markers);
    [ReadOnly, Alias("read")] Task<MapState> Read();
}

[GenerateSerializer, Alias("ui.map-marker")]
public sealed record MapMarker(
    [property: Id(0)] string Id,
    [property: Id(1)] double Lat,
    [property: Id(2)] double Lng,
    [property: Id(3)] string Label);

[GenerateSerializer, Alias("ui.map-state")]
public sealed class MapState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public double Lat { get; set; }
    [Id(3)] public double Lng { get; set; }
    [Id(4)] public double Zoom { get; set; } = 1;
    [Id(5)] public List<MapMarker> Markers { get; set; } = [];
}