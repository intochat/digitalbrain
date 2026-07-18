using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class RecoveryTokenHasher : IRecoveryTokenHasher
{
    private const string HashPrefix = "acct1:";
    private static readonly byte[] _purposeBytes = "TripRadar:recovery-token:v1"u8.ToArray();
    private readonly byte[] _keyBytes;

    public RecoveryTokenHasher(IOptions<EncryptionSettings> encryptionOptions)
    {
        var rootKey = encryptionOptions.Value.UserDataKey;
        if (string.IsNullOrWhiteSpace(rootKey))
        {
            throw new InvalidOperationException("Encryption:UserDataKey must be configured before hashing recovery tokens.");
        }

        _keyBytes = DeriveKey(rootKey);
    }

    public string Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Recovery token must be provided for hashing.", nameof(token));
        }

        using var hmac = new HMACSHA256(_keyBytes);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
        return $"{HashPrefix}{Convert.ToBase64String(hashBytes)}";
    }

    public bool Verify(string token, string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(storedValue))
        {
            return false;
        }

        if (storedValue.StartsWith(HashPrefix, StringComparison.Ordinal))
        {
            return ComparerHelper.Compare(Hash(token), storedValue);
        }

        return ComparerHelper.Compare(storedValue, token);
    }

    private static byte[] DeriveKey(string rootKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(rootKey));
        return hmac.ComputeHash(_purposeBytes);
    }
}
