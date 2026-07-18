namespace TripRadar.Bot.Configuration;

public sealed class BotOptions
{
    public const string SectionName = "Bot";
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
    public string SessionSyncSecret { get; set; } = string.Empty;
    public string InternalApiKey { get; set; } = string.Empty;
    public string MiniAppUrl { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public bool UseMiniAppFlow { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public string CountryCode { get; set; } = "DE";
    public string LanguageCode { get; set; } = "en";
}
