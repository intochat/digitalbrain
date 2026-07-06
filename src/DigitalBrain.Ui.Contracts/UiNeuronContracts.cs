namespace DigitalBrain.Ui.Contracts;

using DigitalBrain.Core;

[Alias("DigitalBrain.Ui.Contracts.IUserSessionNeuron")]
public interface IUserSessionNeuron : INeuron, IHandle<LoginRequest>, IHandle<LogoutRequest>
{
    [Alias("GetSessionAsync")]
    Task<UserSessionState?> GetSessionAsync(string sessionId);
    [Alias("GetSessionByClientIdAsync")]
    Task<UserSessionState?> GetSessionByClientIdAsync(string clientId);
    [Alias("BuildLoginSurfaceAsync")]
    Task<UiSurface> BuildLoginSurfaceAsync(string? clientId = null);
}

[Alias("DigitalBrain.Ui.Contracts.IObservabilityNeuron")]
public interface IObservabilityNeuron : INeuron, IHandle<UiSurface>, IHandle<ClusterActivity>, IHandle<ThreeDGraphUpdate> { }

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.DataChartGenerated")]
public record DataChartGenerated(string RequestId, UiSurface Surface) : Synapse(nameof(DataChartGenerated), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.DataChartFailed")]
public record DataChartFailed(string RequestId, string Reason) : Synapse(nameof(DataChartFailed), DateTimeOffset.UtcNow);
