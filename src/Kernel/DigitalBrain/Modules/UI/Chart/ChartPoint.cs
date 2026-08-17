namespace DigitalBrain.Modules.UI.Chart;

public class ChartPoint
{
    public DateTime Timestamp { get; set; }

    public double Value { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
}