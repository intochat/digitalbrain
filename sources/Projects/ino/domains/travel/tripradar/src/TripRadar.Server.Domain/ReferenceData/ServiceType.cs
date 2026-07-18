namespace TripRadar.Server.Domain.ReferenceData;

public class ServiceType
{
    private ServiceType()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = null!;

    public int? PreferenceCategoryId { get; private set; }

    public PreferenceCategory? PreferenceCategory { get; private set; }
}
