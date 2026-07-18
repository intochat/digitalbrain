namespace TripRadar.Infrastructure.Models
{
    public sealed class StaticSiteSettings
    {
        public string BuildArtifactPath { get; set; } = string.Empty;
        public string IndexDocument { get; set; } = "index.html";
        public string ErrorDocument404Path { get; set; } = "index.html";
    }
}