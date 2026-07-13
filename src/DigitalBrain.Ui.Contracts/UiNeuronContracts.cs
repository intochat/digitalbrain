namespace DigitalBrain.Ui.Contracts;

using DigitalBrain.Core;

[Alias("DigitalBrain.Ui.Contracts.IObservabilityNeuron")]
public interface IObservabilityNeuron : INeuron, IHandle<UiSurface>, IHandle<ClusterActivity>, IHandle<ThreeDGraphUpdate> { }

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.DataChartGenerated")]
public record DataChartGenerated(string RequestId, UiSurface Surface) : Synapse(nameof(DataChartGenerated), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.DataChartFailed")]
public record DataChartFailed(string RequestId, string Reason) : Synapse(nameof(DataChartFailed), DateTimeOffset.UtcNow);
