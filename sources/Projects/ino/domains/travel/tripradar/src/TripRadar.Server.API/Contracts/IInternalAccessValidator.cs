using TripRadar.Server.API.Security;

namespace TripRadar.Server.API.Contracts;

internal interface IInternalAccessValidator
{
    InternalAccessValidationResult Validate(HttpContext httpContext);
}
