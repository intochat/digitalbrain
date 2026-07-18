using Microsoft.Extensions.Options;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;

namespace TripRadar.Bot.Telegram;

internal sealed class MiniAppLinkBuilder(IOptions<BotOptions> options)
{
    public string ForResult(ServiceType type, Guid? requestId)
    {
        var baseUrl = options.Value.MiniAppUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return string.Empty;

        if (requestId is null || requestId == Guid.Empty)
            return ForAlertsScreen();

        var path = type switch
        {
            ServiceType.Flight => "flights/results",
            ServiceType.Hotel => "hotels/results",
            ServiceType.LocalPlaces => "places/results",
            ServiceType.Event => "events/results",
            _ => "alerts"
        };

        return $"{baseUrl.TrimEnd('/')}/{path}?requestId={requestId}";
    }

    public string ForAlertsScreen()
    {
        var baseUrl = options.Value.MiniAppUrl;
        return string.IsNullOrWhiteSpace(baseUrl)
            ? string.Empty
            : $"{baseUrl.TrimEnd('/')}/alerts";
    }
}
