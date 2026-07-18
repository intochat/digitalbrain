namespace TripRadar.Infrastructure.Models
{
    public sealed class ProductionNetworkSettings
    {
        public string VNetAddressSpace { get; set; } = "10.10.0.0/16";
        public string ContainerAppsSubnet { get; set; } = "10.10.0.0/23";
        public string DatabaseSubnet { get; set; } = "10.10.3.0/24";
        public string PrivateEndpointsSubnet { get; set; } = "10.10.2.0/24";
    }
}