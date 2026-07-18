using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Application.Contracts.Services.Authentication;

namespace TripRadar.Server.Infrastructure.Services;

public class TelegramAuthenticationService(
    IUnitOfWork unitOfWork,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    ILogger<TelegramAuthenticationService> logger) : ITelegramAuthenticationService
{
    public async Task<Result<User>> UpsertUserAsync(TelegramAuthDataDTO authData, CancellationToken ct = default)
    {
        try
        {
            var existing = await unitOfWork.UserRepository.GetAuthByTelegramUserIdAsync(authData.Id, ct);
            if (existing != null)
                return Result.Success(existing);

            var newUser = User.CreateFromTelegramAuth(
                authData.Id,
                authData.Username,
                authData.FirstName,
                authData.LastName,
                authData.PhotoUrl,
                UserTierType.Basic.Id);

            await unitOfWork.UserRepository.CreateAsync(newUser, ct);
            await unitOfWork.SaveChangesAsync(ct);

            var now = DateTime.UtcNow;
            await userMonthlyTokenCountRepository.CreateMonthlyTokenCountsAsync(newUser, now.Year, now.Month, newUser.Profile.TimezoneCode, ct);
            await unitOfWork.SaveChangesAsync(ct);

            return Result.Success(newUser);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error upserting Telegram user {TelegramUserId}", authData.Id);
            return Result.Failure<User>(Errors.InternalServerError);
        }
    }
}
