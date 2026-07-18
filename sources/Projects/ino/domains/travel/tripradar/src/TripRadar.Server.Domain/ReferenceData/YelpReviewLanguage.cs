namespace TripRadar.Server.Domain.ReferenceData;

public class YelpReviewLanguage
{
    private YelpReviewLanguage()
    {
    }

    public YelpReviewLanguage(string languageCode, string languageName)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            throw new ArgumentException("Language code is required", nameof(languageCode));
        }

        if (string.IsNullOrWhiteSpace(languageName))
        {
            throw new ArgumentException("Language name is required", nameof(languageName));
        }

        LanguageCode = languageCode.Trim();
        LanguageName = languageName.Trim();
    }

    public string LanguageCode { get; } = null!;

    public string LanguageName { get; } = null!;
}
