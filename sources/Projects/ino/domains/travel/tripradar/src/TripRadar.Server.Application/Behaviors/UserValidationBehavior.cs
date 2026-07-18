using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;

namespace TripRadar.Server.Application.Behaviors;

public class UserValidationBehavior<TRequest, TResponse>(
    IAuthenticatedUserResolver authenticatedUserResolver,
    ICurrentUserContext currentUserContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IAuthorizedRequest
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var validationResult = await authenticatedUserResolver.ResolveValidatedUserAsync(request.Username, cancellationToken);
        if (validationResult.IsFailure)
        {
            return ResultResponseFactory<TResponse>.CreateFailure(validationResult.Error);
        }

        var validatedUser = validationResult.Value;
        if (authenticatedUserResolver.IsRequestIdentityMismatch(validatedUser, request.Username))
        {
            return ResultResponseFactory<TResponse>.CreateFailure(Errors.UnauthorizedAccess);
        }

        currentUserContext.SetUser(validatedUser);
        return await next(cancellationToken);
    }
}
