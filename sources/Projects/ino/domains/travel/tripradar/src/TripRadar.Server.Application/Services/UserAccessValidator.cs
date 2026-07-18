using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Services;

public sealed class UserAccessValidator : IUserAccessValidator
{
    public Result Validate(User user)
    {
        if (!user.Profile.IsEmailConfirmed)
            return Result.Failure(Errors.EmailNotConfirmed);

        return !user.IsActive ? Result.Failure(Errors.UserDisabled) : Result.Success();
    }
}
