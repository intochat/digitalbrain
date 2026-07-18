using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Infrastructure.Contracts;

/// <summary>
/// Service responsible for JWT token generation operations.
/// </summary>
public interface ITokenService
{
    (string AccessToken, string RefreshToken) RotateRefreshToken(User user, DateTime? nowUtc = null);
}
