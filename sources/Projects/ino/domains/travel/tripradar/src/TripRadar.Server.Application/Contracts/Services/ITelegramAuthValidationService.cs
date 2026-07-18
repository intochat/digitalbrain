using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.Contracts.Services;

/// <summary>
/// Service for validating Telegram Login Widget authentication data.
/// </summary>
public interface ITelegramAuthValidationService
{
    /// <summary>
    /// Validates the Telegram authentication data hash using HMAC-SHA256.
    /// </summary>
    /// <param name="authData">The Telegram authentication data containing user info and hash.</param>
    /// <returns>True if the hash is valid and authentication is legitimate; otherwise, false.</returns>
    bool Validate(TelegramAuthDataDTO authData);
}
