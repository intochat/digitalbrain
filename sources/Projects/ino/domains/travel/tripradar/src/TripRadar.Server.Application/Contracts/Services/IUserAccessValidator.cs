using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IUserAccessValidator
{
    Result Validate(User user);
}
