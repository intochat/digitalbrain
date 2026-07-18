namespace TripRadar.MiniApp.Client.Infrastructure.Services.Localization;

public sealed class CityNameLocalizer(FlightTranslationProvider translations)
{
    public string GetName(string? englishCityName) => translations.GetCityName(englishCityName);
}
