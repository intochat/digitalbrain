using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;

namespace TripRadar.Server.Application.Behaviors;

public class LocalizationValidationBehavior<TRequest, TResponse>(
    ILocalizationValidatorService localizationValidatorService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ILocalizationRequest
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request.Localization is not null)
            await localizationValidatorService.ValidateAsync(request.Localization, cancellationToken);

        return await next(cancellationToken);
    }
}
