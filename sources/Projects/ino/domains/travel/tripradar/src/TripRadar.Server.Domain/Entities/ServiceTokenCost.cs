using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class ServiceTokenCost : Entity<int>
{
    private ServiceTokenCost()
    {
    }

    public decimal Cost { get; private set; }

    public int ServiceTypeId { get; private set; }
}
