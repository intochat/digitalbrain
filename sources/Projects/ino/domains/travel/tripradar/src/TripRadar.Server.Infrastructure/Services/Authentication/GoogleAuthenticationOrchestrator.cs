using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Contracts.Authentication;

namespace TripRadar.Server.Infrastructure.Services.Authentication;

public class GoogleAuthenticationOrchestrator(
    IUnitOfWork unitOfWork,
    IGoogleAuthenticationService googleAuthenticationService,
    IClientIpResolver clientIpResolver,
    IUserAuthenticationValidator userAuthenticationValidator,
    ILogger<GoogleAuthenticationOrchestrator> logger) : IGoogleAuthenticationOrchestrator
{
    public async Task<Result<AuthenticationModel>> HandleGoogleLoginAsync(
        string email,
        string firstName,
        string lastName,
        string googleId,
        string? profilePictureUrl,
        Func<User, UnitOfWorkTransactionScope, Task<Result<AuthenticationModel>>> issueTokensCallback)
    {
        await using var scope = await unitOfWork.StartScopeAsync();

        try
        {
            var userIp = clientIpResolver.GetClientIpAddress();
            if (string.IsNullOrEmpty(userIp))
            {
                return Result.Failure<AuthenticationModel>(Errors.UserIpNotValidOrNotProvided);
            }

            var userResult = await googleAuthenticationService.CreateUserAsync(
                email,
                firstName,
                lastName,
                googleId,
                profilePictureUrl,
                userIp,
                cancellationToken: CancellationToken.None);

            if (userResult.IsFailure)
            {
                return Result.Failure<AuthenticationModel>(userResult.Error);
            }

            var user = userResult.Value!;
            var validationResult = userAuthenticationValidator.ValidateForLogin(user);
            if (validationResult.IsFailure)
            {
                return Result.Failure<AuthenticationModel>(validationResult.Error);
            }

            return await issueTokensCallback(user, scope);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Google login process");
            return Result.Failure<AuthenticationModel>(Errors.InternalServerError);
        }
    }
}
