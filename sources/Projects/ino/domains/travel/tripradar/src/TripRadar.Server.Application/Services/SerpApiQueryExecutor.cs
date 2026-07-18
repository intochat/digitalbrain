using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Comms.Core.Errors;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Services;

public sealed class SerpApiQueryExecutor(
    ISerpApiProviderService serpApiProviderService,
    IPreferenceService preferenceService,
    ICurrentUserContext currentUserContext)
    : ISerpApiQueryExecutor
{
    public async Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        ServiceType serviceType,
        Error notFoundError,
        CancellationToken cancellationToken)
        where TRequest : class, ISerpApiRequest
    {
        var userId = currentUserContext.GetRequiredUser().Id;
        var appliedRequestResult = await preferenceService.AddPreferencesAsync(request, userId, serviceType, cancellationToken);
        if (appliedRequestResult.IsFailure)
        {
            return Result.Failure<TResponse>(appliedRequestResult.Error);
        }

        var response = await serpApiProviderService.SearchAsync<TRequest, TResponse>(appliedRequestResult.Value, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<TResponse>(response.Error);
        }

        return response.Value is null
            ? Result.Failure<TResponse>(notFoundError)
            : Result.Success(response.Value);
    }
}
