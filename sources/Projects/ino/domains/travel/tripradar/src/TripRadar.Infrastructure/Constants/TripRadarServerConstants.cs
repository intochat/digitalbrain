namespace TripRadar.Infrastructure.Constants;

public static class TripRadarServerConstants
{
    // DNS
    public const string ZoneName = "tripradar.io";
    public const string DevelopmentCustomDomain = "api-dev.tripradar.io";
    public const string DevelopmentDomainPrefix = "api-dev";
    public const string DevelopmentJobsDomainPrefix = "jobs-dev";

    // Database
    public const string MigrationAssembly = "TripRadar.Server.Db";
    public const string DbContextTypeName = "TripRadar.Server.Db.SetupDbContext";
    public const string AdminUser = "postgres";

    // Pulumi Configuration
    public const string PulumiProjectName = "tripradar";
    public const string DbPasswordConfigKey = "dbPassword";
    public const string DomainVerificationTokenConfigKey = "domainVerificationToken";
    public const string EnvironmentConfigKey = "environment";
    public const string LocationConfigKey = "location";
    public const string ResourceGroupNameConfigKey = "resourceGroupName";
    public const string NamingPrefixConfigKey = "namingPrefix";
    public const string UsePlaceholderImagesConfigKey = "usePlaceholderImages";
    public const string EnableFrontDoorConfigKey = "enableFrontDoor";
    public const string FrontDoorSkuConfigKey = "frontDoorSku";
    public const string UseFrontDoorForApiConfigKey = "useFrontDoorForApi";
    public const string WebsiteHostConfigKey = "domains:websiteHost";
    public const string MiniAppHostConfigKey = "domains:miniAppHost";
    public const string ApiHostConfigKey = "domains:apiHost";
    public const string AllowedCorsOriginsConfigKey = "api:allowedCorsOrigins";
    public const string VNetAddressSpaceConfigKey = "network:vnetAddressSpace";
    public const string ContainerAppsSubnetConfigKey = "network:containerAppsSubnet";
    public const string DatabaseSubnetConfigKey = "network:databaseSubnet";
    public const string PrivateEndpointsSubnetConfigKey = "network:privateEndpointsSubnet";

    // Environment Variables
    public const string DbAdminPasswordEnvVar = "DB_PASSWORD";
    public const string AzureSubscriptionIdEnvVar = "AZURE_SUBSCRIPTION_ID";
    public const string ImageTagEnvVar = "IMAGE_TAG";

    // Default Values
    public const string DefaultImageTag = "latest";
    public const string DefaultContentType = "application/octet-stream";
    public const string DefaultEnvironment = "production";
    public const string DefaultLocation = "westeurope";
    public const string DefaultFrontDoorSku = "Standard_AzureFrontDoor";

    // Application Names
    public const string ApiImagePrefix = "tripradar-api";
    public const string JobsImagePrefix = "tripradar-jobs";

    // Development Environment
    public const string DevelopmentResourceGroup = "tripradar-dev-rg";
    public const string DevelopmentNamingPrefix = "trdev";

    // Network
    public const int DefaultTargetPort = 8080;
    public const string DefaultVNetAddressSpace = "10.10.0.0/16";
    public const string DefaultContainerAppsSubnet = "10.10.0.0/23";
    public const string DefaultDatabaseSubnet = "10.10.3.0/24";
    public const string DefaultPrivateEndpointsSubnet = "10.10.2.0/24";
    public const string DevelopmentVNetAddressSpace = "10.1.0.0/16";
    public const string DevelopmentContainerAppsSubnet = "10.1.0.0/23";
    public const string DevelopmentDatabaseSubnet = "10.1.3.0/24";
    public const string DevelopmentPrivateEndpointsSubnet = "10.1.2.0/24";

    // Storage Containers
    public static readonly string[] DefaultStorageContainers = ["email-assets", "uploads", "documents", "images"];

    // Network Access
    public static readonly List<string> AllowAllIpRanges = ["0.0.0.0/0"];
}
