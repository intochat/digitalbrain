using DigitalBrain.Flutter.Chart;

namespace DigitalBrain.Flutter;

internal sealed record ChartRender(string Title, string Kind, IReadOnlyList<ChartPoint> Points);
