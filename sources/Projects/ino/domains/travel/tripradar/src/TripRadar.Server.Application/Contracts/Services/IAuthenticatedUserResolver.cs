using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IAuthenticatedUserResolver
{
    Task<Result<User>> ResolveValidatedUserAsync(string usernameOrEmail, CancellationToken cancellationToken);

    bool IsRequestIdentityMismatch(User user, string requestUsername);
}
