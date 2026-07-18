using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Infrastructure.Services;

public class UserLookupService(IUnitOfWork unitOfWork, ICredentialValidator credentialValidator) : IUserLookupService
{
    public async Task<Result<User>> FindUserAsync(string usernameOrEmail, CancellationToken cancellationToken = default)
    {
        var user = credentialValidator.IsEmail(usernameOrEmail)
            ? await unitOfWork.UserRepository.GetAuthByEmailAsync(usernameOrEmail, cancellationToken)
            : await unitOfWork.UserRepository.GetAuthByUsernameAsync(usernameOrEmail, cancellationToken);
        return user is null ? Result.Failure<User>(Errors.UserNotFound) : Result.Success(user);
    }

    public async Task<Result<User>> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await unitOfWork.UserRepository.GetAuthByUsernameAsync(username, cancellationToken);
        return user is null ? Result.Failure<User>(Errors.UserNotFound) : Result.Success(user);
    }

    public async Task<Result<User>> FindByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default)
    {
        var user = await unitOfWork.UserRepository.GetAuthByTelegramUserIdAsync(telegramUserId, cancellationToken);
        return user is null ? Result.Failure<User>(Errors.UserNotFound) : Result.Success(user);
    }
}
