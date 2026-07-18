using TripRadar.DeploymentKit.Models.Outputs;
using TripRadar.DeploymentKit.Settings;

namespace TripRadar.DeploymentKit.Interfaces;

/// <summary>
/// Service for managing traffic switching.
/// </summary>
public interface ITrafficManagementService
{
    /// <summary>
    /// Switches traffic between slots.
    /// </summary>
    Task<GreenBlueDeploymentOutputs> SwitchTrafficAsync(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        string targetSlot,
        int trafficPercentage = 100,
        CancellationToken cancellationToken = default);
}

