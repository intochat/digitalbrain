using DigitalBrain.Flutter.Map;

namespace DigitalBrain.Flutter;

internal sealed record MapSet(double Lat, double Lng, double Zoom, IReadOnlyList<MapMarker> Markers);
