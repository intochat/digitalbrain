namespace TripRadar.Server.Domain.ReferenceData;

public class Location
{
    private Location()
    {
    }

    public int LocationId { get; private set; }

    public string RowId { get; private set; } = null!;

    public int? GoogleId { get; private set; }

    public int? GoogleParentId { get; private set; }

    public string Name { get; private set; } = null!;

    public string CanonicalName { get; private set; } = null!;

    public string CountryCode { get; private set; } = null!;

    public string TargetType { get; private set; } = null!;

    public int? Reach { get; private set; }

    public double? GpsLatitude { get; private set; }

    public double? GpsLongitude { get; private set; }
}
