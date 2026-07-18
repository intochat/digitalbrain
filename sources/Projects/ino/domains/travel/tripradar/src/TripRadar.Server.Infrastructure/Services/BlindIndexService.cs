using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services;

public class BlindIndexService : IBlindIndexService
{
    private readonly byte[] _keyBytes;

    public BlindIndexService(IOptions<EncryptionSettings> encryptionOptions)
    {
        var key = encryptionOptions.Value.UserDataKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "Encryption:UserDataKey is required for blind index hashing.");

        _keyBytes = Encoding.UTF8.GetBytes(key);
    }

    public string? ComputeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        using var hmac = new HMACSHA256(_keyBytes);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
