namespace DigitalBrain.Modules.UI.Chart;

public class ChartState
{
    public List<ChartPoint> Points { get; set; } = new();

    public ChartConfig Config { get; set; } = new();
}