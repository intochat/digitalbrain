using Microsoft.Extensions.Options;

namespace TripRadar.Bot.Configuration;

public static class MiniAppConfigEndpoints
{
    public static WebApplication MapMiniAppConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/miniapp/config", (IOptions<BotOptions> options) => Results.Ok(BuildConfig(options.Value)));
        return app;
    }

    public static MiniAppConfig BuildConfig(BotOptions options) => new(options.WebsiteUrl ?? string.Empty);
}

public sealed record MiniAppConfig(string WebsiteUrl);
