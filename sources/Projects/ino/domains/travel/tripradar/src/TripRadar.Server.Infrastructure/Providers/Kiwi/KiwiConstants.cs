namespace TripRadar.Server.Infrastructure.Providers.Kiwi;

internal static class KiwiConstants
{
    public const string CalendarPricesEndpoint = "/umbrella/v2/graphql?featureName=CalendarPricesFetcherQuery";
    public const string ResponseDateFormat = "yyyy-MM-dd'T'HH:mm:ss";
    public const string DefaultLocale = "en";
    public const string DefaultMarket = "bg";
    public const string DefaultPartner = "skypicker";
    public const string UserAgent = "Mozilla/5.0 TripRadar Kiwi price calendar";
}
