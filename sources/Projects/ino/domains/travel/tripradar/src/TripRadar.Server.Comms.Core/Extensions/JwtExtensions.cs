using System.Security.Cryptography;

namespace TripRadar.Server.Comms.Core.Extensions;

public static class JwtExtensions
{
    public static bool IsValidJwtFormat(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.');
        return parts.Length == 3 && parts.All(part => !string.IsNullOrWhiteSpace(part));
    }

    public static string GenerateToken()
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }
}
