using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Contracts.Authentication;

namespace TripRadar.Server.Infrastructure.Services;

public class GoogleAuthenticationService(IUnitOfWork unitOfWork, IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository, ILogger<GoogleAuthenticationService> logger) : IGoogleAuthenticationService
{
    public async Task<Result<User>> CreateUserAsync(
        string email,
        string firstName,
        string lastName,
        string googleId,
        string? profilePictureUrl,
        string userIpAddress,
        CancellationToken cancellationToken = default)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
        try
        {
            var existingUser = await unitOfWork.UserRepository.GetByGoogleIdAsync(googleId, cancellationToken);
            if (existingUser != null)
            {
                await scope.CommitAsync(cancellationToken);
                return Result.Success(existingUser);
            }

            var existingEmailUser = await unitOfWork.UserRepository.GetByEmailAsync(email, cancellationToken);
            if (existingEmailUser != null)
            {
                existingEmailUser.UpdateGoogleData(googleId, profilePictureUrl);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await scope.CommitAsync(cancellationToken);
                return Result.Success(existingEmailUser);
            }

            var newUser = User.CreateFromGoogleAuth(
                email,
                firstName,
                lastName,
                googleId,
                profilePictureUrl,
                userIpAddress,
                1,
                UserTierType.Basic.Id);

            await unitOfWork.UserRepository.CreateAsync(newUser, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var now = DateTime.UtcNow;
            await userMonthlyTokenCountRepository.CreateMonthlyTokenCountsAsync(newUser, now.Year, now.Month, newUser.Profile.TimezoneCode, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await scope.CommitAsync(cancellationToken);
            return Result.Success(newUser);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error upserting Google user for email {Email}", email);
            return Result.Failure<User>(Errors.InternalServerError);
        }
    }
}
