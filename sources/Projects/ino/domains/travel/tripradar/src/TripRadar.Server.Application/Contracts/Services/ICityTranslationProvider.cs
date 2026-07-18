namespace TripRadar.Server.Application.Contracts.Services;

public interface ICityTranslationProvider
{
    string? GetEnglishCityName(string localizedName);
}
