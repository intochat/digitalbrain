using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class RefreshTokenHasher : IRefreshTokenHasher
{
    private const string HashPrefix = "rt1:";
    private readonly byte[] _keyBytes;

    public RefreshTokenHasher(IOptions<Jwt> jwtOptions)
    {
        var key = jwtOptions.Value.RefreshTokenKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Jwt:RefreshTokenKey must be configured before hashing refresh tokens.");
        }

        _keyBytes = Encoding.UTF8.GetBytes(key);
        if (_keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"Refresh token HMAC key must be at least 32 bytes (256 bits). " +
                $"Current key length: {_keyBytes.Length} bytes.");
        }
    }

    public string Hash(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token must be provided for hashing.", nameof(refreshToken));
        }

        using var hmac = new HMACSHA256(_keyBytes);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
        return $"{HashPrefix}{Convert.ToBase64String(hashBytes)}";
    }

    public bool Verify(string refreshToken, string storedValue, out bool isLegacy)
    {
        isLegacy = false;

        if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(storedValue))
        {
            return false;
        }

        if (storedValue.StartsWith(HashPrefix, StringComparison.Ordinal))
        {
            var expected = Hash(refreshToken);
            return ComparerHelper.Compare(expected, storedValue);
        }

        isLegacy = true;
        return ComparerHelper.Compare(storedValue, refreshToken);
    }
}
