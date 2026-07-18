using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using AppErrors = TripRadar.Server.Application.ApplicationErrors.Errors;

namespace TripRadar.Server.Infrastructure.Services.UserLimits;

public sealed class UserLimitUserLookup(IUnitOfWork unitOfWork)
{
    public async Task<Result<User>> GetByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByUsernameForLimitsAsync(username, cancellationToken);
        return user is null ? Result.Failure<User>(AppErrors.UserNotFound) : Result.Success(user);
    }
}
