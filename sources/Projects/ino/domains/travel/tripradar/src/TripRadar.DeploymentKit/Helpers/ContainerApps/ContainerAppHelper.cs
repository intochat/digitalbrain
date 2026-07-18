using TripRadar.DeploymentKit.Constants;
using TripRadar.DeploymentKit.Models.Outputs;
using TripRadar.DeploymentKit.Settings;
using TripRadar.DeploymentKit.Utilities;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;

namespace TripRadar.DeploymentKit.Helpers.ContainerApps;

/// <summary>
/// Helper for creating Container Apps environments.
/// </summary>
public static class ContainerAppHelper
{
    /// <summary>
    /// Creates a Managed Environment for Container Apps.
    /// </summary>
    public static ManagedEnvironment CreateEnvironment(
        string environmentName,
        InfrastructureSettings settings,
        Input<string> resourceGroup,
        MonitoringOutputs monitoring)
    {
        return new ManagedEnvironment(environmentName, new ManagedEnvironmentArgs
        {
            ResourceGroupName = resourceGroup,
            Location = settings.Location,
            AppLogsConfiguration = new AppLogsConfigurationArgs
            {
                Destination = ContainerAppConstants.LogAnalyticsDestination,
                LogAnalyticsConfiguration = new LogAnalyticsConfigurationArgs
                {
                    CustomerId = monitoring.LogAnalyticsWorkspaceId,
                    SharedKey = monitoring.LogAnalyticsWorkspacePrimaryKey
                }
            },
            Tags = ResourceTagHelper.GetStandardTags(settings.Environment, ContainerAppConstants.ContainerAppsEnvironmentType)
        });
    }
}



