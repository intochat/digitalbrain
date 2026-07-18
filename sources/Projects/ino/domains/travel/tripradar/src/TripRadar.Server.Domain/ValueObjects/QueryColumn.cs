using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.ValueObjects;

public class QueryColumn : ValueObject
{
    private QueryColumn()
    {
    }

    public QueryColumn(string name, bool isActive)
    {
        Name = name.Trim();
        IsActive = isActive;
    }

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Name;
        yield return IsActive;
    }
}
