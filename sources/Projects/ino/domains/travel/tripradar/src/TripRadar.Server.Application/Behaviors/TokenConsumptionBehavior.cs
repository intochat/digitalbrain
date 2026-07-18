using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;

namespace TripRadar.Server.Application.Behaviors;

public class TokenConsumptionBehavior<TRequest, TResponse>(
    IUserLimitService userLimitService,
    ICurrentUserContext currentUserContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITokenConsumingRequest
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var userTokenResult = currentUserContext.User is { } user &&
                              string.Equals(user.Profile.Username, request.Username, StringComparison.Ordinal)
            ? await userLimitService.VerifyLimitEligibilityAsync(user, request.ServiceType, cancellationToken)
            : await userLimitService.VerifyLimitEligibilityAsync(request.Username, request.ServiceType, cancellationToken);

        return userTokenResult.IsFailure ? ResultResponseFactory<TResponse>.CreateFailure(userTokenResult.Error) : await next(cancellationToken);
    }
}
