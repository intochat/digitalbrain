using DigitalBrain.SDK.DigitalBrain.Ui.Visuals;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals.Materials;

public interface IMaterialPlanBroadcaster
{
    Task BroadcastAsync(string clientId, MaterialPlan plan);
    IAsyncEnumerable<MaterialPlan> SubscribeAsync(string clientId, CancellationToken ct);
}
