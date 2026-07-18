using TripRadar.DeploymentKit.Constants;
using TripRadar.DeploymentKit.Models.Outputs;
using TripRadar.DeploymentKit.Settings;

namespace TripRadar.DeploymentKit.Extensions;

/// <summary>
/// Extension methods for green-blue deployment operations
/// </summary>
public static class GreenBlueExtensions
{
    /// <summary>
    /// Gets the staging URL based on the target slot configuration.
    /// </summary>
    public static Output<string> GetStagingUrl(this InfrastructureSettings settings, SlotOutputs greenSlot, SlotOutputs blueSlot) =>
        settings.GreenBlueDeployment?.TargetSlot.ToLowerInvariant() switch
        {
            GreenBlueConstants.GreenSlotName => greenSlot.AppUrl,
            _ => blueSlot.AppUrl
        };
}

