namespace TripRadar.Infrastructure.Models
{
    public sealed class DomainsSettings
    {
        public string WebsiteHost { get; set; } = string.Empty;
        public string MiniAppHost { get; set; } = string.Empty;
        public string ApiHost { get; set; } = string.Empty;
    }
}