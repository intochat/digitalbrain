namespace TripRadar.Server.Domain.ReferenceData;

public class PreferenceCategory
{
    private PreferenceCategory()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; }
}
