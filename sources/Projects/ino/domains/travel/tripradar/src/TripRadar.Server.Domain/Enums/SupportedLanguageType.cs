using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class SupportedLanguageType(int id, string name) : Enumeration(id, name)
{
    private static readonly SupportedLanguageType _english = new(1, "en");
    private static readonly SupportedLanguageType _russian = new(2, "ru");

    public static string[] GetAllLanguageCodes() => [_english.Name, _russian.Name];
}
