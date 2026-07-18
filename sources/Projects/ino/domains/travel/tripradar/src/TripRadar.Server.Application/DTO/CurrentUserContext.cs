using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.DTO;

public class CurrentUserContext : ICurrentUserContext
{
    public User? User { get; private set; }

    public void SetUser(User user) => User = user;

    public User GetRequiredUser() => User ?? throw new InvalidOperationException("Current user is not available in the request context.");
}
