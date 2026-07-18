using TripRadar.DeploymentKit.Models.Outputs;
using TripRadar.DeploymentKit.Settings;

namespace TripRadar.DeploymentKit.Interfaces;

public interface IFrontDoorDeployer
{
    Task<FrontDoorOutputs?> CreateFoundationAsync(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        CancellationToken cancellationToken = default);

    Task<FrontDoorOutputs?> ConfigureRoutingAsync(
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        FrontDoorOutputs foundation,
        StorageOutputs storage,
        ContainerAppsOutputs containerApps,
        CancellationToken cancellationToken = default);
}


