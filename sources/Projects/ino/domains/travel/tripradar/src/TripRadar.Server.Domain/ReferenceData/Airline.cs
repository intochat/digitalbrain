namespace TripRadar.Server.Domain.ReferenceData;

public class Airline
{
    private Airline()
    {
    }

    public Airline(string airlineCode, string airlineName, string? searchAliases = null, bool isAlliance = false, bool isActive = true)
    {
        AirlineCode = airlineCode.Trim().ToUpperInvariant();
        AirlineName = airlineName.Trim();
        SearchAliases = string.IsNullOrWhiteSpace(searchAliases) ? null : searchAliases.Trim().ToLowerInvariant();
        IsAlliance = isAlliance;
        IsActive = isActive;
    }

    public string AirlineCode { get; } = null!;

    public string AirlineName { get; } = null!;

    public string? SearchAliases { get; }

    public bool IsAlliance { get; }

    public bool IsActive { get; }
}
