using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Infrastructure.Contracts.Authentication;

public interface IGoogleAuthenticationService
{
    Task<Result<User>> CreateUserAsync(string email, string firstName, string lastName, string googleId, string? profilePictureUrl, string userIpAddress, CancellationToken cancellationToken = default);
}

