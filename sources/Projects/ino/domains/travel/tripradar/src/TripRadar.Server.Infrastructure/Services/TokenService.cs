using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services;

public class TokenService : ITokenService
{
    private const string SecurityStampClaimType = "security_stamp";
    private readonly Jwt _jwt;
    private readonly Lazy<SigningCredentials> _signingCredentials;
    private readonly IRefreshTokenHasher _refreshTokenHasher;

    public TokenService(IOptions<Jwt> jwtSettings, IRefreshTokenHasher refreshTokenHasher)
    {
        _jwt = jwtSettings.Value;
        _signingCredentials = new Lazy<SigningCredentials>(CreateSigningCredentials);
        _refreshTokenHasher = refreshTokenHasher;
    }

    private string GenerateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Role, "User"),
            new("permissions", "read:data,write:data")
        };

        if (!string.IsNullOrWhiteSpace(user.Profile.Username))
            claims.Add(new Claim(JwtRegisteredClaimNames.Name, user.Profile.Username));

        if (!string.IsNullOrWhiteSpace(user.Profile.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Profile.Email));

        if (!string.IsNullOrWhiteSpace(user.Profile.SecurityStamp))
            claims.Add(new Claim(SecurityStampClaimType, user.Profile.SecurityStamp));

        var expiry = _jwt.DurationInMinutes > 0
            ? DateTime.UtcNow.AddMinutes(_jwt.DurationInMinutes)
            : DateTime.UtcNow.AddMonths(_jwt.DurationInMonths);

        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, DateTime.UtcNow, expiry, _signingCredentials.Value);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken() => JwtExtensions.GenerateToken();

    public (string AccessToken, string RefreshToken) RotateRefreshToken(User user, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var newRefreshToken = GenerateRefreshToken();
        user.UpdateTokenData(_refreshTokenHasher.Hash(newRefreshToken), now.AddDays(7));

        var accessToken = GenerateAccessToken(user);
        return (accessToken, newRefreshToken);
    }

    private SigningCredentials CreateSigningCredentials()
    {
        var keyBytes = Encoding.UTF8.GetBytes(_jwt.Key);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"JWT signing key must be at least 32 bytes (256 bits) for HMAC-SHA256. " +
                $"Current key length: {keyBytes.Length} bytes. " +
                "Please configure a cryptographically secure key of sufficient length.");
        }

        var securityKey = new SymmetricSecurityKey(keyBytes);
        var keyHash = SHA256.HashData(keyBytes);
        var base64Hash = Convert.ToBase64String(keyHash);
        securityKey.KeyId = base64Hash.Length >= 16 ? base64Hash[..16] : base64Hash;
        return new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    }
}
