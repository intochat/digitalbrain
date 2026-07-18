using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Contracts.Requests.Create;
using TripRadar.Server.API.Security;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.API.Services;

public class RefreshTokenRequestResolver(ICurrentRequestUserProvider currentRequestUserProvider, IOptions<Jwt> jwtOptions) : IRefreshTokenRequestResolver
{
    private readonly Jwt _jwtSettings = jwtOptions.Value;

    public string? ResolveRefreshToken(HttpContext httpContext, CreateRefreshTokenRequest request) =>
        !string.IsNullOrWhiteSpace(request.RefreshToken) ? request.RefreshToken : httpContext.Request.Cookies[AuthCookieHelper.RefreshTokenCookieName];

    public bool TryResolveUserId(HttpContext httpContext, CreateRefreshTokenRequest request, out long userId) =>
        currentRequestUserProvider.TryGetUserId(out userId) || TryGetUserIdFromAccessToken(httpContext, request, out userId);

    private bool TryGetUserIdFromAccessToken(HttpContext httpContext, CreateRefreshTokenRequest request, out long userId)
    {
        userId = 0;
        var accessToken = ResolveAccessToken(httpContext, request);
        if (string.IsNullOrWhiteSpace(accessToken))
            return false;

        if (string.IsNullOrWhiteSpace(_jwtSettings.Key) ||
            string.IsNullOrWhiteSpace(_jwtSettings.Issuer) ||
            string.IsNullOrWhiteSpace(_jwtSettings.Audience))
            return false;

        try
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwtSettings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key)),
                ValidateLifetime = false
            };

            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(accessToken, tokenValidationParameters, out _);

            return principal.TryGetUserId(out userId);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveAccessToken(HttpContext httpContext, CreateRefreshTokenRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.AccessToken))
            return request.AccessToken;

        var accessTokenFromCookie = httpContext.Request.Cookies[AuthCookieHelper.AccessTokenCookieName];
        return !string.IsNullOrWhiteSpace(accessTokenFromCookie) ? accessTokenFromCookie : TryGetBearerTokenFromAuthorizationHeader(httpContext.Request);
    }

    private static string? TryGetBearerTokenFromAuthorizationHeader(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            return null;

        var value = authorizationHeader.ToString();
        const string bearerPrefix = "Bearer ";
        return value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) ? value[bearerPrefix.Length..].Trim() : null;
    }
}
