using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class Tier : Entity<int>
{
    private Tier()
    {
    }

    public Tier(string name, decimal tokensPerMonthLimit)
    {
        Name = name;
        TokensPerMonthLimit = tokensPerMonthLimit;
    }

    public new int Id { get; private set; }

    public string Name { get; private set; } = null!;

    public decimal TokensPerMonthLimit { get; private set; }

    // Use only in EF
    private ICollection<User> Users { get; set; } = new List<User>();
}
