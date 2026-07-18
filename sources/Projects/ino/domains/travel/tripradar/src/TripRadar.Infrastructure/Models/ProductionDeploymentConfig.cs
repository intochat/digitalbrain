namespace TripRadar.Infrastructure.Models;

public sealed class ProductionDeploymentConfig
{
    public string Environment { get; set; } = "production";
    public string Location { get; set; } = "westeurope";
    public string ResourceGroupName { get; set; } = string.Empty;
    public string NamingPrefix { get; set; } = string.Empty;
    public bool UsePlaceholderImages { get; set; } = true;
    public bool EnableFrontDoor { get; set; } = true;
    public string FrontDoorSku { get; set; } = "Standard_AzureFrontDoor";
    public DomainsSettings Domains { get; set; } = new();
    public StaticSiteSettings Website { get; set; } = new();
    public StaticSiteSettings MiniApp { get; set; } = new();
    public ProductionApiSettings Api { get; set; } = new();
    public ProductionNetworkSettings Network { get; set; } = new();
}