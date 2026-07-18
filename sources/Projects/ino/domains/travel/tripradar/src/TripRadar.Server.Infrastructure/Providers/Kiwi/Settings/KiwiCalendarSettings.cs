namespace TripRadar.Server.Infrastructure.Providers.Kiwi.Settings;

public sealed class KiwiCalendarSettings
{
    public string BaseUrl { get; set; } = null!;

    public int RequestTimeoutSeconds { get; set; }

    public string DefaultLocale { get; set; } = KiwiConstants.DefaultLocale;

    public string Market { get; set; } = KiwiConstants.DefaultMarket;

    public string Partner { get; set; } = KiwiConstants.DefaultPartner;
}
