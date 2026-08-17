namespace DigitalBrain.Modules.UI.Chart;

public class ChartConfig
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = "line"; // line, bar, pie, scatter, area
    public string XAxisLabel { get; set; } = "X";
    public string YAxisLabel { get; set; } = "Y";
}