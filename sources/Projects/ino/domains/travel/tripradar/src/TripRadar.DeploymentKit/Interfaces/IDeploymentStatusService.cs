using TripRadar.DeploymentKit.Models.Outputs;
using TripRadar.DeploymentKit.Settings;

namespace TripRadar.DeploymentKit.Interfaces;

/// <summary>
/// Service for getting deployment status.
/// </summary>
public interface IDeploymentStatusService
{
    /// <summary>
    /// Gets the deployment status using a resolved resource group name.
    /// </summary>
    Task<GreenBlueDeploymentOutputs> GetDeploymentStatusAsync(
        InfrastructureSettings settings,
        string resourceGroupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a specific slot.
    /// </summary>
    Task<SlotOutputs> GetSlotStatusAsync(string resourceGroupName, string appName, string slotName);
}

