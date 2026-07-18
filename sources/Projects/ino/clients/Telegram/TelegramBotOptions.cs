namespace Ino.Telegram.Host;

/// <summary>
/// Bound from the <c>Telegram</c> configuration section. <see cref="BotToken"/>
/// flows in as an Aspire secret parameter; the rest are optional.
///
/// <para>When <see cref="BotToken"/> is empty the bot's webhook + command
/// registration is skipped entirely — the host still boots and serves Flutter
/// from wwwroot/. This preserves the "git clean -fdx + aspire run" demo path
/// without forcing the operator to provision a bot token just to see the
/// silo come up.</para>
///
/// <para>Webhook resolution order:
/// <list type="number">
///   <item><see cref="WebhookUrl"/> if set — public HTTPS origin Telegram will POST to.</item>
///   <item>Otherwise <see cref="NgrokApiUrl"/> if set — local dev tunnel,
///         WebhookSetupService polls the ngrok admin API for the public URL.</item>
///   <item>Otherwise no webhook is registered. The bot won't receive updates
///         until one of the above is configured.</item>
/// </list>
/// The same resolved URL is also surfaced as the WebApp launch URL so /start
/// → "Open ino" loads the Flutter bundle Telegram serves from this host.</para>
/// </summary>
public sealed class TelegramBotOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
    public string NgrokApiUrl { get; set; } = string.Empty;
}
