using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Infrastructure.Models;

namespace TripRadar.Server.Infrastructure.Contracts.Authentication;

public interface IAuthenticationService
{
    Task<Result<AuthenticationModel>> LoginAsync(TokenModel model);

    Task<Result<AuthenticationModel>> GetRefreshTokenAsync(long userId, string refreshToken);

    Task<Result<AuthenticationModel>> GoogleLoginAsync(string email, string firstName, string lastName, string googleId, string? profilePictureUrl);

    Task<Result<AuthenticationModel>> GetTokenByTelegramAuthAsync(TelegramAuthDataDTO authData);

    Task<Result<AuthenticationModel>> GetTokenByTelegramUserIdAsync(long telegramUserId);

    Task<Result<AuthenticationModel>> GetTokenByUsernameAsync(string username);
}
