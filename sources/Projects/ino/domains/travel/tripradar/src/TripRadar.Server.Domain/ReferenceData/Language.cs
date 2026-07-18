namespace TripRadar.Server.Domain.ReferenceData;

public class Language
{
    private Language()
    {
    }

    public Language(string languageCode, string languageName, bool isInternal = false)
    {
        LanguageCode = languageCode.Trim();
        LanguageName = languageName.Trim();
        IsInternal = isInternal;
    }

    public string LanguageCode { get; } = null!;

    public string LanguageName { get; } = null!;

    public bool IsInternal { get; }
}
