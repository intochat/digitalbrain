namespace Ino.Telegram.Host;

/// <summary>
/// Singleton holding runtime state resolved after startup — primarily the
/// public HTTPS URL Telegram uses for webhook callbacks AND the URL the
/// "Open ino" WebApp button launches. Written by <c>WebhookSetupService</c>
/// once the public origin is known (either from configuration or by polling
/// the ngrok admin API), read by the bot's update handler when answering
/// /start.
/// </summary>
public sealed class TelegramBotState
{
    string? _publicUrl;
    public string? PublicUrl
    {
        get => _publicUrl;
        set => Interlocked.Exchange(ref _publicUrl, value);
    }
    // Trailing "/" so Telegram opens the site root — the SPA fallback serves
    // index.html for "/" and Flutter's GoRouter matches its home route.
    // Linking to "/index.html" instead made GoRouter throw "no routes for
    // location: /index.html" because that literal path isn't registered.
    public string? MiniAppUrl => PublicUrl is not null ? $"{PublicUrl}/" : null;
}
