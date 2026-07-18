namespace TripRadar.Server.API.Security;

internal static class AuthCookieHelper
{
    public const string AccessTokenCookieName = "accessToken";
    public const string RefreshTokenCookieName = "refreshToken";
    public const string AntiforgeryCookieName = "antiforgery";
    public const string AntiforgeryRequestTokenCookieName = "XSRF-TOKEN";
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";

    private const string ClientTypeHeader = "X-Client-Type";
    private const string ApiClientType = "api";
    private const string ReturnAuthPayloadHeader = "X-Return-Auth-Payload";

    public static bool IsApiClient(HttpRequest request) =>
        request.Headers.TryGetValue(ClientTypeHeader, out var clientType) && string.Equals(clientType.FirstOrDefault(), ApiClientType, StringComparison.OrdinalIgnoreCase);

    public static bool HasAuthCookies(HttpRequest request) => request.Cookies.ContainsKey(AccessTokenCookieName) || request.Cookies.ContainsKey(RefreshTokenCookieName);

    public static bool ShouldReturnAuthPayload(HttpRequest request)
    {
        if (IsApiClient(request))
        {
            return true;
        }

        return request.Headers.TryGetValue(ReturnAuthPayloadHeader, out var shouldReturnPayload) &&
               bool.TryParse(shouldReturnPayload.FirstOrDefault(), out var parsedValue) &&
               parsedValue;
    }

    public static void SetAccessTokenCookie(HttpResponse response, IHostEnvironment environment, string accessToken, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        response.Cookies.Append(AccessTokenCookieName, accessToken, CreateCookieOptions(environment, DateTimeOffset.UtcNow.Add(lifetime)));
    }

    public static void SetRefreshTokenCookie(HttpResponse response, IHostEnvironment environment, string refreshToken, TimeSpan lifetime)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        response.Cookies.Append(RefreshTokenCookieName, refreshToken, CreateCookieOptions(environment, DateTimeOffset.UtcNow.Add(lifetime)));
    }

    public static void SetAntiforgeryRequestTokenCookie(HttpResponse response, IHostEnvironment environment, string requestToken, DateTimeOffset expires)
    {
        if (string.IsNullOrWhiteSpace(requestToken))
        {
            return;
        }

        response.Cookies.Append(AntiforgeryRequestTokenCookieName, requestToken, CreateAntiforgeryRequestTokenCookieOptions(environment, expires));
    }

    public static void ClearAuthCookies(HttpResponse response, IHostEnvironment environment)
    {
        var expired = DateTimeOffset.UtcNow.AddDays(-1);

        response.Cookies.Delete(AccessTokenCookieName, CreateCookieOptions(environment, expired));
        response.Cookies.Delete(RefreshTokenCookieName, CreateCookieOptions(environment, expired));
        response.Cookies.Delete(AntiforgeryCookieName, CreateAntiforgeryCookieOptions(environment, expired));
        response.Cookies.Delete(AntiforgeryRequestTokenCookieName, CreateAntiforgeryRequestTokenCookieOptions(environment, expired));

        response.Cookies.Append(AccessTokenCookieName, string.Empty, CreateCookieOptions(environment, expired));
        response.Cookies.Append(RefreshTokenCookieName, string.Empty, CreateCookieOptions(environment, expired));
        response.Cookies.Append(AntiforgeryCookieName, string.Empty, CreateAntiforgeryCookieOptions(environment, expired));
        response.Cookies.Append(AntiforgeryRequestTokenCookieName, string.Empty, CreateAntiforgeryRequestTokenCookieOptions(environment, expired));
    }

    public static CookieOptions CreateAntiforgeryCookieOptions(IHostEnvironment environment, DateTimeOffset expires) =>
        new()
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() && !environment.IsEnvironment("Test") && !environment.IsEnvironment("Testing"),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
            IsEssential = true
        };

    public static CookieOptions CreateAntiforgeryRequestTokenCookieOptions(IHostEnvironment environment, DateTimeOffset expires) =>
        new()
        {
            HttpOnly = false,
            Secure = !environment.IsDevelopment() && !environment.IsEnvironment("Test") && !environment.IsEnvironment("Testing"),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
            IsEssential = true
        };

    private static CookieOptions CreateCookieOptions(IHostEnvironment environment, DateTimeOffset expires) =>
        new()
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() && !environment.IsEnvironment("Test") && !environment.IsEnvironment("Testing"),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
            IsEssential = true
        };
}
