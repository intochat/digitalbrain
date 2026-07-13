namespace DigitalBrain.Ui.Contracts;

using DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.DataChartGenerated")]
public record DataChartGenerated(string RequestId, UiSurface Surface) : Synapse(nameof(DataChartGenerated), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.DataChartFailed")]
public record DataChartFailed(string RequestId, string Reason) : Synapse(nameof(DataChartFailed), DateTimeOffset.UtcNow);
