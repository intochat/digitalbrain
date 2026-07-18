namespace TripRadar.Server.Domain.ReferenceData;

public class TripAdvisorDomain
{
    private TripAdvisorDomain()
    {
    }

    public TripAdvisorDomain(string domainName, string title, string locale)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            throw new ArgumentException("Domain name is required", nameof(domainName));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new ArgumentException("Locale is required", nameof(locale));
        }

        DomainName = domainName.Trim();
        Title = title.Trim();
        Locale = locale.Trim();
    }

    public string DomainName { get; } = null!;

    public string Title { get; } = null!;

    public string Locale { get; } = null!;
}
