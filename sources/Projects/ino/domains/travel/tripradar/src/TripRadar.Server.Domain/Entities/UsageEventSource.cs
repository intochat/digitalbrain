using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class UsageEventSource : Entity<int>
{
    private UsageEventSource()
    {
    }

    public UsageEventSource(int id, string name, string? description = null, bool isActive = true)
    {
        Id = id;
        Name = name;
        Description = description;
        IsActive = isActive;
    }

    public new int Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }
}
