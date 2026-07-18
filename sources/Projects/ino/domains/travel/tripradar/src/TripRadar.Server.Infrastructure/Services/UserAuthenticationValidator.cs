using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Contracts;

namespace TripRadar.Server.Infrastructure.Services;

public class UserAuthenticationValidator(
    ILogger<UserAuthenticationValidator> logger,
    IRefreshTokenHasher refreshTokenHasher)
    : IUserAuthenticationValidator
{
    public Result ValidateForLogin(User user)
    {
        if (!user.Profile.IsEmailConfirmed)
            return Result.Failure(Errors.EmailNotConfirmed);

        if (!user.Profile.TelegramUserId.HasValue || string.IsNullOrWhiteSpace(user.Profile.Username))
            return Result.Failure(Errors.TelegramRequired with { Reason = user.Profile.Email });

        return !user.IsActive ? Result.Failure(Errors.UserDisabled) : Result.Success();
    }

    public Result ValidateRefreshToken(User user, string refreshToken)
    {
        if (user.Profile.RefreshTokenExpiryTime <= DateTime.UtcNow)
        {
            logger.LogWarning("Refresh token expired. Expiry: {ExpiryTime}, Current: {CurrentTime}", user.Profile.RefreshTokenExpiryTime, DateTime.UtcNow);
            return Result.Failure(Errors.RefreshTokenExpired);
        }

        if (refreshTokenHasher.Verify(refreshToken, user.Profile.RefreshToken, out var isLegacy))
        {
            if (isLegacy)
                logger.LogInformation("Legacy refresh token matched for user {UserId}. Token will be rotated on use.", user.Id);
            return Result.Success();
        }

        logger.LogWarning("Refresh token mismatch. Token length - Stored: {StoredLength}, Provided: {ProvidedLength}",user.Profile.RefreshToken?.Length ?? 0, refreshToken.Length);
        return Result.Failure(Errors.RefreshTokenNotFound);
    }
}
