namespace TripRadar.Server.Domain.ReferenceData;

public class GoogleDomain
{
    private GoogleDomain()
    {
    }

    public GoogleDomain(string domainName, string languageCode, string countryCode, string countryName)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            throw new ArgumentException("Domain name is required", nameof(domainName));
        }

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            throw new ArgumentException("Language code is required", nameof(languageCode));
        }

        if (string.IsNullOrWhiteSpace(countryCode))
        {
            throw new ArgumentException("Country code is required", nameof(countryCode));
        }

        if (string.IsNullOrWhiteSpace(countryName))
        {
            throw new ArgumentException("Country name is required", nameof(countryName));
        }

        DomainName = domainName.Trim();
        LanguageCode = languageCode.Trim();
        CountryCode = countryCode.Trim();
        CountryName = countryName.Trim();
    }

    public string DomainName { get; } = null!;

    public string LanguageCode { get; } = null!;

    public string CountryCode { get; } = null!;

    public string CountryName { get; } = null!;
}
