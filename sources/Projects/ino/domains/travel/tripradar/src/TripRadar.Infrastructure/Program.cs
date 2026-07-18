using Pulumi;
using TripRadar.DeploymentKit.Deployer;
using TripRadar.DeploymentKit.Helpers;
using TripRadar.DeploymentKit.Models;
using TripRadar.DeploymentKit.Settings;
using TripRadar.DeploymentKit.Utilities;
using TripRadar.Infrastructure.Constants;

namespace TripRadar.Infrastructure;

internal static class Program
{
    private static Task<int> Main() => Deployment.RunAsync(async () =>
    {
        Config config = new(TripRadarServerConstants.PulumiProjectName);
        string dbPassword = EnvironmentConfigurationHelper.GetSecretFromConfigOrEnv(
            config,
            TripRadarServerConstants.DbPasswordConfigKey,
            TripRadarServerConstants.DbAdminPasswordEnvVar);
        string subscriptionId = EnvironmentConfigurationHelper.GetRequiredEnvironmentVariable(TripRadarServerConstants.AzureSubscriptionIdEnvVar);
        string imageTag = Environment.GetEnvironmentVariable(TripRadarServerConstants.ImageTagEnvVar) ?? TripRadarServerConstants.DefaultImageTag;
        string environment = config.Get(TripRadarServerConstants.EnvironmentConfigKey) ?? TripRadarServerConstants.DefaultEnvironment;

        if (string.Equals(environment, "development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment, "dev", StringComparison.OrdinalIgnoreCase))
        {
            InfrastructureSettings developmentSettings =
                TripRadarDeploymentConfiguration.CreateDevelopmentSettings(subscriptionId, dbPassword, imageTag);
            var infrastructure = await InfrastructureDeployer.DeployAsync(developmentSettings);

            await ConfigureDevelopmentDomainsAsync(config, developmentSettings, infrastructure);

            Output<string> apiOriginHostname = infrastructure.ContainerApps.ApiApp.Configuration.Apply(
                configuration => configuration?.Ingress?.Fqdn ?? string.Empty);

            return new Dictionary<string, object?>
            {
                ["resourceGroupName"] = infrastructure.ResourceGroupName,
                ["acrLoginServer"] = infrastructure.AcrLoginServer,
                ["apiOriginHostname"] = apiOriginHostname,
                ["jobsInternalFqdn"] = infrastructure.JobsInternalFqdn
            };
        }

        var productionConfig = TripRadarDeploymentConfiguration.LoadProductionConfig(config);
        var settings = TripRadarDeploymentConfiguration.CreateProductionSettings(productionConfig, subscriptionId, dbPassword, imageTag);
        var productionInfrastructure = await InfrastructureDeployer.DeployAsync(settings);

        Output<string> productionApiOriginHostname = productionInfrastructure.ContainerApps.ApiApp.Configuration.Apply(
            configuration => configuration?.Ingress?.Fqdn ?? string.Empty);

        return new Dictionary<string, object?>
        {
            ["resourceGroupName"] = productionInfrastructure.ResourceGroupName,
            ["acrLoginServer"] = productionInfrastructure.AcrLoginServer,
            ["apiOriginHostname"] = productionApiOriginHostname,
            ["websiteHost"] = productionConfig.Domains.WebsiteHost,
            ["miniAppHost"] = productionConfig.Domains.MiniAppHost,
            ["apiHost"] = productionConfig.Domains.ApiHost,
            ["websiteStorageAccountName"] = productionInfrastructure.Storage.WebsiteAccountName,
            ["miniAppStorageAccountName"] = productionInfrastructure.Storage.MiniAppAccountName,
            ["websiteStorageEndpoint"] = productionInfrastructure.Storage.WebsitePrimaryEndpoint,
            ["miniAppStorageEndpoint"] = productionInfrastructure.Storage.MiniAppPrimaryEndpoint,
            ["miniAppUrl"] = $"https://{productionConfig.Domains.MiniAppHost}"
        };
    });

    private static Task ConfigureDevelopmentDomainsAsync(
        Config config,
        InfrastructureSettings settings,
        TripRadar.DeploymentKit.Models.Outputs.InfrastructureDeploymentOutputs infrastructure)
    {
        string? verificationToken = config.Get(TripRadarServerConstants.DomainVerificationTokenConfigKey);
        bool useFrontDoorForApi = config.GetBoolean(TripRadarServerConstants.UseFrontDoorForApiConfigKey) ?? false;

        if (string.IsNullOrWhiteSpace(verificationToken))
        {
            return Task.CompletedTask;
        }

        if (!useFrontDoorForApi)
        {
            DnsConfig apiDnsConfig = TripRadarDeploymentConfiguration.CreateApiDnsConfig(
                settings,
                infrastructure.ContainerApps.ApiApp.Configuration.Apply(configuration => configuration?.Ingress?.Fqdn ?? string.Empty));
            var (apiCnameRecord, apiTxtRecord) = DeploymentHelper.CreateDnsRecords(apiDnsConfig, [verificationToken]);
            DeploymentHelper.BindCustomDomain(apiDnsConfig, apiCnameRecord, apiTxtRecord, null);
        }

        DnsConfig jobsDnsConfig = TripRadarDeploymentConfiguration.CreateJobsDnsConfig(
            settings,
            infrastructure.ContainerApps.JobsApp.Configuration.Apply(configuration => configuration?.Ingress?.Fqdn ?? string.Empty));
        var (jobsCnameRecord, jobsTxtRecord) = DeploymentHelper.CreateDnsRecords(jobsDnsConfig, [verificationToken]);
        DeploymentHelper.BindCustomDomain(jobsDnsConfig, jobsCnameRecord, jobsTxtRecord, null);

        return Task.CompletedTask;
    }
}
