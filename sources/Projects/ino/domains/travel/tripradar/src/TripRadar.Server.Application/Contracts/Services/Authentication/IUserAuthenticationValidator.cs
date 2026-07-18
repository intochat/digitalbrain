using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface IUserAuthenticationValidator
{
    Result ValidateForLogin(User user);

    Result ValidateRefreshToken(User user, string refreshToken);
}
