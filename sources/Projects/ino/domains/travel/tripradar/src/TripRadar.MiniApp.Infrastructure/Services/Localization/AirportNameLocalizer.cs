namespace TripRadar.MiniApp.Client.Infrastructure.Services.Localization;

public sealed class AirportNameLocalizer(FlightTranslationProvider translations)
{
    public string GetName(string? iataCode, string? fallbackName = null) => translations.GetAirportName(iataCode, fallbackName);
}
