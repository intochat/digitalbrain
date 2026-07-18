using TripRadar.DeploymentKit.Constants;
using TripRadar.DeploymentKit.Models.Outputs;
using TripRadar.DeploymentKit.Settings;
using TripRadar.DeploymentKit.Utilities;
using Pulumi.AzureNative.App;
using Pulumi.AzureNative.App.Inputs;

namespace TripRadar.DeploymentKit.Helpers;

/// <summary>
/// Helper class for Container App operations.
/// </summary>
public static class ContainerAppHelper
{
    /// <summary>
    /// Creates the managed environment for container apps.
    /// </summary>
    public static ManagedEnvironment CreateManagedEnvironment(string environmentName, Input<string> resourceGroup, InfrastructureSettings settings, MonitoringOutputs monitoring)
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

    /// <summary>
    /// Creates environment variables for a container app slot.
    /// </summary>
    /// <param name="settings">Infrastructure settings.</param>
    /// <param name="cache">Cache outputs.</param>
    /// <param name="monitoring">Monitoring outputs.</param>
    /// <param name="slotSettings">Slot settings.</param>
    /// <returns>Array of environment variable arguments.</returns>
    public static EnvironmentVarArgs[] CreateEnvironmentVariables(
        InfrastructureSettings settings,
        CacheOutputs cache,
        MonitoringOutputs monitoring,
        SlotSettings slotSettings)
    {
        var baseEnvVars = new List<EnvironmentVarArgs>
        {
            new() { Name = "ASPNETCORE_URLS", Value = DeploymentConstants.Urls.DefaultAspNetCoreUrls },
            new() { Name = "ASPNETCORE_ENVIRONMENT", Value = settings.Environment },
            new() { Name = "ConnectionStrings__AppDb", SecretRef = DeploymentConstants.ContainerApps.PostgresConnectionStringSecretName },
            new() { Name = "ConnectionStrings__Redis", Value = cache.ConnectionString },
            new() { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = monitoring.ApplicationInsightsConnectionString },
            new() { Name = "OTEL_EXPORTER_OTLP_ENDPOINT", Value = DeploymentConstants.Urls.LocalhostOtlpEndpoint },
            new() { Name = "DEPLOYMENT_SLOT", Value = slotSettings.SlotName },
            new() { Name = "DEPLOYMENT_VERSION", Value = slotSettings.VersionString }
        };

        baseEnvVars.AddRange(slotSettings.EnvironmentVariables.Select(envVar => new EnvironmentVarArgs { Name = envVar.Key, Value = envVar.Value }));

        return baseEnvVars.ToArray();
    }
}



