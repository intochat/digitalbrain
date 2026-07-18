namespace TripRadar.Server.Domain.ReferenceData;

public class Country
{
    private Country()
    {
    }

    public Country(string countryCode, string countryName)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length is < 2 or > 3)
        {
            throw new ArgumentException("Country code must be 2-3 letters", nameof(countryCode));
        }

        if (string.IsNullOrWhiteSpace(countryName))
        {
            throw new ArgumentException("Country name is required", nameof(countryName));
        }

        CountryCode = countryCode.ToUpperInvariant();
        CountryName = countryName.Trim();
    }

    public string CountryCode { get; } = null!;

    public string CountryName { get; } = null!;
}
