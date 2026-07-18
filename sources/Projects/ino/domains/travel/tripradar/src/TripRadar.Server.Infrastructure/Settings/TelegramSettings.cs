namespace TripRadar.Server.Infrastructure.Settings;

/// <summary>
/// Configuration settings for Telegram bot integration.
/// </summary>
public class TelegramSettings
{
    public const string DefaultOidcAuthority = "https://oauth.telegram.org";

    /// <summary>
    /// Telegram bot token used for validating Telegram Login Widget authentication.
    /// This should be stored securely in Azure Key Vault.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Telegram website login client identifier.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Telegram website login client secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// OpenID Connect authority for Telegram website login.
    /// </summary>
    public string OidcAuthority { get; set; } = DefaultOidcAuthority;

    /// <summary>
    /// Public Telegram Mini App URL used in bot buttons.
    /// </summary>
    public string WebAppUrl { get; set; } = string.Empty;

    /// <summary>
    /// Public website URL used for website handoffs from Telegram.
    /// </summary>
    public string WebsiteUrl { get; set; } = string.Empty;
}
