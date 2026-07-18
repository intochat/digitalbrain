using HotChocolate.Execution;
using ErrorCodes = TripRadar.Server.Comms.Core.Constants.ErrorCodes;

namespace TripRadar.Server.API.Filters;

internal class GraphQlErrorFilter(IServiceProvider serviceProvider) : IErrorFilter
{
    private const string GenericErrorMessage = "An internal error occurred. Please try again later.";

    public IError OnError(IError error)
    {
        var httpContextAccessor = serviceProvider.GetService<IHttpContextAccessor>();
        if (httpContextAccessor?.HttpContext is { } ctx)
        {
            ctx.Items["GraphQL:Success"] = false;
            if (!ctx.Response.HasStarted) ctx.Response.StatusCode = ResolveStatusCode(error);
        }

        if (error.Exception == null)
            return error.Code switch
            {
                ErrorCodes.InvalidRequest => error.WithMessage(error.Message).WithCode(ErrorCodes.InvalidRequest),
                ErrorCodes.ObjectNotFound => error.WithMessage(error.Message).WithCode(ErrorCodes.ObjectNotFound),
                _ => error
            };

        if (error.Exception is GraphQLException)
        {
            return string.IsNullOrWhiteSpace(error.Code)
                ? error.WithMessage(error.Message)
                : error.WithMessage(error.Message).WithCode(error.Code);
        }

        var hostEnvironment = serviceProvider.GetService<IHostEnvironment>();
        if (hostEnvironment is null || !hostEnvironment.IsDevelopment())
            return error.WithMessage(GenericErrorMessage);

        var exceptionMessage = $"{error.Exception.GetType().Name}: {error.Exception.Message}";
        if (error.Exception.InnerException != null)
            exceptionMessage += $" Inner: {error.Exception.InnerException.GetType().Name}: {error.Exception.InnerException.Message}";

        return error.WithMessage(exceptionMessage);

    }

    private static int ResolveStatusCode(IError error)
    {
        if (error.Exception is not null)
            return StatusCodes.Status500InternalServerError;

        return error.Code switch
        {
            ErrorCodes.InvalidRequest => StatusCodes.Status400BadRequest,
            ErrorCodes.ObjectNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };
    }
}
