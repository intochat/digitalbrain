using TripRadar.Server.Comms.Core.Contracts.Exceptions;
using TripRadar.Server.Comms.Core.Exceptions;
using ErrorCodes = TripRadar.Server.Comms.Core.Constants.ErrorCodes;

namespace TripRadar.Server.API.Filters;

internal class ValidationFilter : IExceptionDetails
{
    public ExceptionDetails? GetExceptionDetails(Exception exception) =>
        exception switch
        {
            ValidationException validationException => new ExceptionDetails(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidRequest,
                "One or more validation errors have occurred.",
                validationException.Errors),
            _ => null
        };
}
