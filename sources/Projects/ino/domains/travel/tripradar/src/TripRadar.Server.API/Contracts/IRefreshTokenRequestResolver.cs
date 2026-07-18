using TripRadar.Server.API.Contracts.Requests.Create;

namespace TripRadar.Server.API.Contracts;

public interface IRefreshTokenRequestResolver
{
    string? ResolveRefreshToken(HttpContext httpContext, CreateRefreshTokenRequest request);

    bool TryResolveUserId(HttpContext httpContext, CreateRefreshTokenRequest request, out long userId);
}
