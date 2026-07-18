using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts;

public interface ICurrentUserContext
{
    User? User { get; }

    void SetUser(User user);

    User GetRequiredUser();
}
