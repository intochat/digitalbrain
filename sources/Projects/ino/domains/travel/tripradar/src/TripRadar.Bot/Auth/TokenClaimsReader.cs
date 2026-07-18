using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TripRadar.Bot.Models;

namespace TripRadar.Bot.Auth;

internal static class TokenClaimsReader
{
    private static readonly JwtSecurityTokenHandler Handler = new();

    public static BotResult<(string Username, DateTimeOffset ExpiresAtUtc)> ReadUsernameAndExpiry(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || !Handler.CanReadToken(accessToken))
            return BotResult<(string Username, DateTimeOffset ExpiresAtUtc)>.Fail("Access token format is invalid.");

        var token = Handler.ReadJwtToken(accessToken);

        var username = token.Claims.FirstOrDefault(static claim =>
            claim.Type is ClaimTypes.Name
                or JwtRegisteredClaimNames.Name
                or "unique_name"
                or "username")?.Value;

        if (string.IsNullOrWhiteSpace(username))
            return BotResult<(string Username, DateTimeOffset ExpiresAtUtc)>.Fail("Access token does not include username claim.");

        var expiry = ResolveExpiry(token);
        return BotResult<(string Username, DateTimeOffset ExpiresAtUtc)>.Ok((username, expiry));
    }

    private static DateTimeOffset ResolveExpiry(JwtSecurityToken token)
    {
        var expClaimValue = token.Claims.FirstOrDefault(static claim => claim.Type == JwtRegisteredClaimNames.Exp)?.Value;
        if (long.TryParse(expClaimValue, out var exp))
            return DateTimeOffset.FromUnixTimeSeconds(exp);

        return token.ValidTo > DateTime.MinValue
            ? new DateTimeOffset(token.ValidTo, TimeSpan.Zero)
            : DateTimeOffset.UtcNow.AddMinutes(5);
    }
}
