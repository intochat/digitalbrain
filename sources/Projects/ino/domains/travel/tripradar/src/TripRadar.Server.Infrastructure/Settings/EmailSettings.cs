namespace TripRadar.Server.Infrastructure.Settings;

public class EmailSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string EmailLogoUrl { get; set; } = string.Empty;
    public string BlobStorageUrl { get; set; } = string.Empty;
    public string BlobStorageSasToken { get; set; } = string.Empty;
    public string RedirectUrl { get; set; } = string.Empty;
    public SocialLinksSettings SocialLinks { get; set; } = new();
}
