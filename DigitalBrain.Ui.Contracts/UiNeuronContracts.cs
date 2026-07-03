namespace DigitalBrain.Core;

public interface IUserSessionNeuron : INeuron, IHandle<LoginRequest>, IHandle<LogoutRequest>
{
    Task<UserSessionState?> GetSessionAsync(string sessionId);
    Task<UiSurface> BuildLoginSurfaceAsync(string? clientId = null);
}

public interface IObservabilityNeuron : INeuron, IHandle<UiSurface>, IHandle<ClusterActivity>, IHandle<ThreeDGraphUpdate> { }

[GenerateSerializer]
public record DataChartGenerated(string RequestId, UiSurface Surface) : Synapse(nameof(DataChartGenerated), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record DataChartFailed(string RequestId, string Reason) : Synapse(nameof(DataChartFailed), DateTimeOffset.UtcNow);
