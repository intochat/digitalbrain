using TripRadar.Server.API.Contracts.Responses.Create;
using TripRadar.Server.API.Contracts.Responses.Get;

namespace TripRadar.Server.API.Contracts;

public interface IAuthResponseBuilder
{
    GetLoginResponse BuildLoginResponse(HttpContext httpContext, string? token, string? refreshToken);

    ActivateUserResponse BuildActivationResponse(HttpContext httpContext, string? token, string? refreshToken, string email, string username);
}
