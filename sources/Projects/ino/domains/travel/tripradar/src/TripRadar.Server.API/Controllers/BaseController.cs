using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TripRadar.Server.Comms.Core.Errors;
using TripRadar.Server.Comms.Core.Extensions;
using Error = TripRadar.Server.Comms.Core.Errors.Error;

namespace TripRadar.Server.API.Controllers;

[Authorize]
[ApiController]
[ApiVersion("1.0")]
[ApiConventionType(typeof(DefaultApiConventions))]
public abstract class BaseController : ControllerBase
{
    internal BadRequestObjectResult BadRequest(Error error) => BadRequest(GetErrorResponse(error));

    internal IActionResult HandleError(Error error)
    {
        var errorResponse = GetErrorResponse(error);

        return error.Code switch
        {
            var code when code.EndsWith("_NOT_FOUND") => NotFound(errorResponse),
            "UNAUTHORIZED" => Unauthorized(errorResponse),
            "FORBIDDEN" => Forbid(),
            "UNAUTHORIZED_ACCESS" => StatusCode(403, errorResponse),
            "FEEDBACK_RATE_LIMIT_EXCEEDED" => StatusCode(429, errorResponse),
            var code when code.EndsWith("_UNAUTHORIZED_ACCESS") => StatusCode(403, errorResponse),
            _ => BadRequest(errorResponse)
        };
    }

    protected string GetUsername() => User.GetUsername() ?? throw new InvalidOperationException("Authenticated username was not found after RequireUsername filter validation.");

    private static ErrorResponse GetErrorResponse(Error error) => new() { ErrorCode = error.Code, ErrorReason = error.Reason };
}
