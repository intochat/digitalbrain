namespace TripRadar.Server.Domain.ReferenceData;

public class OpenTableDomain
{
    private OpenTableDomain()
    {
    }

    public OpenTableDomain(string domainName, string country)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            throw new ArgumentException("Domain name is required", nameof(domainName));
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            throw new ArgumentException("Country is required", nameof(country));
        }

        DomainName = domainName.Trim();
        Country = country.Trim();
    }

    public string DomainName { get; } = null!;

    public string Country { get; } = null!;
}
