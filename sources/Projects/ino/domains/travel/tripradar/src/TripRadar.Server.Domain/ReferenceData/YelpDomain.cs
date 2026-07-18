namespace TripRadar.Server.Domain.ReferenceData;

public class YelpDomain
{
    private YelpDomain()
    {
    }

    public YelpDomain(string domainName, string locale)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            throw new ArgumentException("Domain name is required", nameof(domainName));
        }

        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new ArgumentException("Locale is required", nameof(locale));
        }

        DomainName = domainName.Trim();
        Locale = locale.Trim();
    }

    public string DomainName { get; } = null!;

    public string Locale { get; } = null!;
}
