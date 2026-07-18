using Pulumi;
using TripRadar.DeploymentKit.Enums;
using TripRadar.DeploymentKit.Models;
using TripRadar.DeploymentKit.Settings;
using TripRadar.Infrastructure.Constants;
using TripRadar.Infrastructure.Helpers;
using TripRadar.Infrastructure.Models;

namespace TripRadar.Infrastructure;

public static class TripRadarDeploymentConfiguration
{
    public static InfrastructureSettings CreateDevelopmentSettings(string subscriptionId, string dbPassword, string imageTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageTag);

        return new InfrastructureSettings
        {
            Environment = "development",
            Location = TripRadarServerConstants.DefaultLocation,
            SubscriptionId = subscriptionId,
            ResourceGroupName = TripRadarServerConstants.DevelopmentResourceGroup,
            NamingPrefix = TripRadarServerConstants.DevelopmentNamingPrefix,
            Database = new DatabaseSettings
            {
                AdminPassword = dbPassword,
                Password = dbPassword,
                SkuName = DatabaseSkuType.StandardB1ms,
                BackupRetentionDays = 7,
                GeoRedundantBackup = false,
                Version = PostgreSqlVersionType.Version16,
                AdminUser = TripRadarServerConstants.AdminUser,
                AdminUsername = TripRadarServerConstants.AdminUser
            },
            Migration = new MigrationSettings
            {
                Enabled = true,
                MigrationType = MigrationType.EfCore,
                MigrationAssembly = TripRadarServerConstants.MigrationAssembly,
                DbContextTypeName = TripRadarServerConstants.DbContextTypeName,
                AutoRunOnDeployment = true
            },
            Storage = new StorageSettings
            {
                AccountTier = StorageAccountTierType.Standard,
                ReplicationType = StorageReplicationType.StandardLrs,
                EnableHttpsTrafficOnly = true,
                MinimumTlsVersion = TlsVersionType.Tls12,
                AllowBlobPublicAccess = false
            },
            BlobStorage = new BlobStorageSettings
            {
                AccessTier = BlobAccessTierType.Hot,
                EnableVersioning = false,
                EnableChangeFeed = false,
                EnableSoftDelete = true,
                SoftDeleteRetentionDays = 7,
                ContainerNames = [.. TripRadarServerConstants.DefaultStorageContainers],
                AllowPublicAccess = false,
                DefaultContentType = TripRadarServerConstants.DefaultContentType,
                EnableLifecycleManagement = false
            },
            EventHubs = new EventHubsSettings
            {
                SkuName = EventHubsSkuType.Basic,
                Capacity = 1,
                MessageRetentionInDays = 1,
                PartitionCount = 2
            },
            Monitoring = new MonitoringSettings
            {
                LogRetentionDays = 30,
                ApplicationInsightsType = ApplicationInsightsType.Web,
                EnableDetailedMetrics = false,
                EnableLiveMetrics = true
            },
            Network = new NetworkSettings
            {
                IsInternalEnvironment = false,
                VNetAddressSpace = TripRadarServerConstants.DevelopmentVNetAddressSpace,
                ContainerAppsSubnet = TripRadarServerConstants.DevelopmentContainerAppsSubnet,
                DatabaseSubnet = TripRadarServerConstants.DevelopmentDatabaseSubnet,
                PrivateEndpointsSubnet = TripRadarServerConstants.DevelopmentPrivateEndpointsSubnet,
                EnableDdosProtection = false
            },
            Container = new ContainerSettings
            {
                MinReplicas = 1,
                MaxReplicas = 3,
                CpuLimit = 0.5,
                MemoryLimit = 1,
                UsePlaceholderImages = false,
                ApiImageTag = $"{TripRadarServerConstants.ApiImagePrefix}:{imageTag}",
                JobsImageTag = $"{TripRadarServerConstants.JobsImagePrefix}:{imageTag}",
                AutoScaling = new AutoScalingSettings
                {
                    MinReplicas = 1,
                    MaxReplicas = 3,
                    EnableMemoryScaling = false
                },
                IngressSettings = new IngressSettings
                {
                    External = true,
                    TargetPort = TripRadarServerConstants.DefaultTargetPort,
                    AllowInsecure = false,
                    IpSecurityRestrictions =
                    [
                        new IpSecurityRestrictionSettings
                        {
                            Name = "AllowAll",
                            IpAddressRange = TripRadarServerConstants.AllowAllIpRanges[0],
                            Action = "Allow",
                            Description = "Azure Front Door validated by middleware"
                        }
                    ]
                }
            },
            KeyVault = CreateKeyVaultSettings(dbPassword),
            Api = new ApiSettings
            {
                BaseUrl = $"https://{TripRadarServerConstants.DevelopmentCustomDomain}",
                EnableHttps = true,
                EnableHttpsRedirection = true,
                EnableSwagger = false,
                RateLimitRequestsPerMinute = 120,
                EnableDetailedErrors = false
            },
            ValidationMode = ValidationMode.Basic,
            SkipAzureAuthValidation = false
        };
    }

    public static ProductionDeploymentConfig LoadProductionConfig(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var domains = new DomainsSettings
        {
            WebsiteHost = config.Require(TripRadarServerConstants.WebsiteHostConfigKey),
            MiniAppHost = config.Require(TripRadarServerConstants.MiniAppHostConfigKey),
            ApiHost = config.Require(TripRadarServerConstants.ApiHostConfigKey)
        };

        var corsOrigins = ParseCsv(config.Get(TripRadarServerConstants.AllowedCorsOriginsConfigKey));
        if (corsOrigins.Count == 0)
        {
            corsOrigins.Add($"https://{domains.WebsiteHost}");
            corsOrigins.Add($"https://{domains.MiniAppHost}");
        }

        return new ProductionDeploymentConfig
        {
            Environment = config.Get(TripRadarServerConstants.EnvironmentConfigKey) ?? TripRadarServerConstants.DefaultEnvironment,
            Location = config.Get(TripRadarServerConstants.LocationConfigKey) ?? TripRadarServerConstants.DefaultLocation,
            ResourceGroupName = config.Require(TripRadarServerConstants.ResourceGroupNameConfigKey),
            NamingPrefix = config.Require(TripRadarServerConstants.NamingPrefixConfigKey),
            UsePlaceholderImages = config.GetBoolean(TripRadarServerConstants.UsePlaceholderImagesConfigKey) ?? true,
            EnableFrontDoor = config.GetBoolean(TripRadarServerConstants.EnableFrontDoorConfigKey) ?? true,
            FrontDoorSku = config.Get(TripRadarServerConstants.FrontDoorSkuConfigKey) ?? TripRadarServerConstants.DefaultFrontDoorSku,
            Domains = domains,
            Website = new StaticSiteSettings(),
            MiniApp = new StaticSiteSettings(),
            Api = new ProductionApiSettings
            {
                AllowedCorsOrigins = corsOrigins
            },
            Network = new ProductionNetworkSettings
            {
                VNetAddressSpace = config.Get(TripRadarServerConstants.VNetAddressSpaceConfigKey) ?? TripRadarServerConstants.DefaultVNetAddressSpace,
                ContainerAppsSubnet = config.Get(TripRadarServerConstants.ContainerAppsSubnetConfigKey) ?? TripRadarServerConstants.DefaultContainerAppsSubnet,
                DatabaseSubnet = config.Get(TripRadarServerConstants.DatabaseSubnetConfigKey) ?? TripRadarServerConstants.DefaultDatabaseSubnet,
                PrivateEndpointsSubnet = config.Get(TripRadarServerConstants.PrivateEndpointsSubnetConfigKey) ?? TripRadarServerConstants.DefaultPrivateEndpointsSubnet
            }
        };
    }

    public static InfrastructureSettings CreateProductionSettings(ProductionDeploymentConfig config, string subscriptionId, string dbPassword, string imageTag)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageTag);

        return new InfrastructureSettings
        {
            Environment = config.Environment,
            Location = config.Location,
            SubscriptionId = subscriptionId,
            ResourceGroupName = config.ResourceGroupName,
            NamingPrefix = config.NamingPrefix,
            Database = new DatabaseSettings
            {
                AdminPassword = dbPassword,
                Password = dbPassword,
                SkuName = DatabaseSkuType.StandardB1ms,
                BackupRetentionDays = 7,
                GeoRedundantBackup = false,
                Version = PostgreSqlVersionType.Version16,
                AdminUser = TripRadarServerConstants.AdminUser,
                AdminUsername = TripRadarServerConstants.AdminUser
            },
            Migration = new MigrationSettings
            {
                Enabled = true,
                MigrationType = MigrationType.EfCore,
                MigrationAssembly = TripRadarServerConstants.MigrationAssembly,
                DbContextTypeName = TripRadarServerConstants.DbContextTypeName,
                AutoRunOnDeployment = false
            },
            Storage = new StorageSettings
            {
                AccountTier = StorageAccountTierType.Standard,
                ReplicationType = StorageReplicationType.StandardLrs,
                EnableHttpsTrafficOnly = true,
                MinimumTlsVersion = TlsVersionType.Tls12,
                AllowBlobPublicAccess = false
            },
            BlobStorage = new BlobStorageSettings
            {
                AccessTier = BlobAccessTierType.Hot,
                EnableVersioning = false,
                EnableChangeFeed = false,
                EnableSoftDelete = true,
                SoftDeleteRetentionDays = 7,
                ContainerNames = [.. TripRadarServerConstants.DefaultStorageContainers],
                AllowPublicAccess = false,
                DefaultContentType = TripRadarServerConstants.DefaultContentType,
                EnableLifecycleManagement = false
            },
            EventHubs = new EventHubsSettings
            {
                SkuName = EventHubsSkuType.Basic,
                Capacity = 1,
                MessageRetentionInDays = 1,
                PartitionCount = 2
            },
            Monitoring = new MonitoringSettings
            {
                LogRetentionDays = 30,
                ApplicationInsightsType = ApplicationInsightsType.Web,
                EnableDetailedMetrics = false,
                EnableLiveMetrics = true
            },
            Network = new NetworkSettings
            {
                IsInternalEnvironment = false,
                VNetAddressSpace = config.Network.VNetAddressSpace,
                ContainerAppsSubnet = config.Network.ContainerAppsSubnet,
                DatabaseSubnet = config.Network.DatabaseSubnet,
                PrivateEndpointsSubnet = config.Network.PrivateEndpointsSubnet,
                EnableDdosProtection = false
            },
            Container = new ContainerSettings
            {
                MinReplicas = 1,
                MaxReplicas = 3,
                CpuLimit = 0.5,
                MemoryLimit = 1,
                UsePlaceholderImages = config.UsePlaceholderImages,
                ApiImageTag = $"{TripRadarServerConstants.ApiImagePrefix}:{imageTag}",
                JobsImageTag = $"{TripRadarServerConstants.JobsImagePrefix}:{imageTag}",
                AutoScaling = new AutoScalingSettings
                {
                    MinReplicas = 1,
                    MaxReplicas = 3,
                    EnableMemoryScaling = false
                },
                IngressSettings = new IngressSettings
                {
                    External = true,
                    TargetPort = TripRadarServerConstants.DefaultTargetPort,
                    AllowInsecure = false,
                    IpSecurityRestrictions = []
                }
            },
            WebsiteStaticSite = new StaticSiteHostSettings
            {
                Enabled = true,
                SiteName = "website",
                HostName = config.Domains.WebsiteHost,
                IndexDocument = config.Website.IndexDocument,
                ErrorDocument404Path = config.Website.ErrorDocument404Path
            },
            MiniAppStaticSite = new StaticSiteHostSettings
            {
                Enabled = true,
                SiteName = "miniapp",
                HostName = config.Domains.MiniAppHost,
                IndexDocument = config.MiniApp.IndexDocument,
                ErrorDocument404Path = config.MiniApp.ErrorDocument404Path
            },
            KeyVault = CreateKeyVaultSettings(dbPassword),
            Api = new ApiSettings
            {
                BaseUrl = $"https://{config.Domains.ApiHost}",
                EnableHttps = true,
                EnableHttpsRedirection = true,
                EnableSwagger = false,
                CorsOrigins = string.Join(',', config.Api.AllowedCorsOrigins.Distinct(StringComparer.OrdinalIgnoreCase)),
                RateLimitRequestsPerMinute = 120,
                EnableDetailedErrors = false
            },
            FrontDoor = config.EnableFrontDoor
                ? new FrontDoorSettings
                {
                    Enabled = true,
                    SkuName = config.FrontDoorSku,
                    EnableCustomDomain = true,
                    CustomDomainHostName = config.Domains.ApiHost,
                    WebsiteHostName = config.Domains.WebsiteHost,
                    MiniAppHostName = config.Domains.MiniAppHost,
                    ApiHostName = config.Domains.ApiHost,
                    EnableWaf = true,
                    HealthProbePath = "/health"
                }
                : null,
            ValidationMode = ValidationMode.Basic,
            SkipAzureAuthValidation = false
        };
    }

    public static DnsConfig CreateApiDnsConfig(InfrastructureSettings settings, Input<string> containerAppFqdn)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new DnsConfig
        {
            CustomDomain = TripRadarServerConstants.DevelopmentDomainPrefix,
            ZoneName = TripRadarServerConstants.ZoneName,
            ContainerAppName = $"{settings.NamingPrefix}-api",
            ResourceGroupName = settings.ResourceGroupName,
            EnvironmentName = $"{settings.NamingPrefix}-cae-{settings.Environment.ToLowerInvariant()}",
            ContainerAppFqdn = containerAppFqdn
        };
    }

    public static DnsConfig CreateJobsDnsConfig(InfrastructureSettings settings, Input<string> containerAppFqdn)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new DnsConfig
        {
            CustomDomain = TripRadarServerConstants.DevelopmentJobsDomainPrefix,
            ZoneName = TripRadarServerConstants.ZoneName,
            ContainerAppName = $"{settings.NamingPrefix}-jobs",
            ResourceGroupName = settings.ResourceGroupName,
            EnvironmentName = $"{settings.NamingPrefix}-cae-{settings.Environment.ToLowerInvariant()}",
            ContainerAppFqdn = containerAppFqdn
        };
    }

    private static KeyVaultSettings CreateKeyVaultSettings(string dbPassword)
    {
        var keyVaultSettings = new KeyVaultSettings
        {
            EnableSoftDelete = true,
            SoftDeleteRetentionDays = 90,
            EnablePurgeProtection = true,
            SkuName = KeyVaultSkuType.Standard,
            ApplyToContainerApps = true,
            EnablePublicNetworkAccess = false,
            EnablePrivateEndpoints = true,
            NetworkAccess = new NetworkAccessRulesSettings
            {
                DefaultAction = NetworkAccessActionType.Deny,
                AllowedIpRanges = [],
                AllowedSubnetIds = []
            }
        };

        IReadOnlyDictionary<string, string> secrets = EnvironmentSecretsProvider.GetKeyVaultSecretsFromEnvironment(dbPassword);
        foreach ((string key, string value) in secrets)
        {
            keyVaultSettings.Secrets[key] = value;
        }

        return keyVaultSettings;
    }

    private static List<string> ParseCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
