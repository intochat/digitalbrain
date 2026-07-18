using Microsoft.Extensions.Options;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.API.Contracts.Responses.Get;
using TripRadar.Server.API.Security;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.API.Services;

public class AuthResponseBuilder(IOptions<Jwt> jwtOptions, IHostEnvironment hostEnvironment) : IAuthResponseBuilder
{
    private const int RefreshTokenLifetimeDays = 30;
    private readonly Jwt _jwtSettings = jwtOptions.Value;

    public GetLoginResponse BuildLoginResponse(HttpContext httpContext, string? token, string? refreshToken)
    {
        var shouldReturnAuthPayload = AuthCookieHelper.ShouldReturnAuthPayload(httpContext.Request);
        var shouldSetAuthCookies = !AuthCookieHelper.IsApiClient(httpContext.Request);

        if (shouldSetAuthCookies)
        {
            SetAuthCookies(httpContext, token, refreshToken);
        }

        return new GetLoginResponse
        {
            Token = shouldReturnAuthPayload ? token : null,
            RefreshToken = shouldReturnAuthPayload ? refreshToken : null
        };
    }

    public ActivateUserResponse BuildActivationResponse(HttpContext httpContext, string? token, string? refreshToken, string email, string username)
    {
        var shouldReturnAuthPayload = AuthCookieHelper.ShouldReturnAuthPayload(httpContext.Request);
        var shouldSetAuthCookies = !AuthCookieHelper.IsApiClient(httpContext.Request);

        if (shouldSetAuthCookies)
        {
            SetAuthCookies(httpContext, token, refreshToken);
        }

        return new ActivateUserResponse
        {
            Token = shouldReturnAuthPayload ? token : null,
            RefreshToken = shouldReturnAuthPayload ? refreshToken : null,
            Email = email,
            Username = username
        };
    }

    private void SetAuthCookies(HttpContext httpContext, string? accessToken, string? refreshToken)
    {
        var accessTokenLifetime = TimeSpan.FromMinutes(Math.Max(_jwtSettings.DurationInMinutes, 1));
        var refreshTokenLifetime = TimeSpan.FromDays(RefreshTokenLifetimeDays);

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            AuthCookieHelper.SetAccessTokenCookie(httpContext.Response, hostEnvironment, accessToken, accessTokenLifetime);
        }

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            AuthCookieHelper.SetRefreshTokenCookie(httpContext.Response, hostEnvironment, refreshToken, refreshTokenLifetime);
        }
    }
}
