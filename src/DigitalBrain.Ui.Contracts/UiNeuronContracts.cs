namespace DigitalBrain.Ui.Contracts;

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.DataChartGenerated")]
public record DataChartGenerated(string RequestId, UiSurface Surface);
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.DataChartFailed")]
public record DataChartFailed(string RequestId, string Reason);
