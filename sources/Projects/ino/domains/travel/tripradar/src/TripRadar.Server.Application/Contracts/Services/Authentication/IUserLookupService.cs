using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services.Authentication;

/// <summary>
/// Service for looking up users by various identifiers.
/// Centralizes user lookup logic to eliminate duplication across authentication flows.
/// </summary>
public interface IUserLookupService
{
    /// <summary>
    /// Finds a user by username or email, automatically detecting the credential type.
    /// </summary>
    Task<Result<User>> FindUserAsync(string usernameOrEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by username only.
    /// </summary>
    Task<Result<User>> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by Telegram user id.
    /// </summary>
    Task<Result<User>> FindByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);
}
