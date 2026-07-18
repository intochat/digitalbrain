using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.Behaviors;

public class PostSuccessUsageEventBehavior<TRequest, TResponse>(
    ICurrentUserContext currentUserContext,
    IServiceTokenCostRepository serviceTokenCostRepository,
    IUsageEventWriter usageEventWriter,
    IUsageSourceResolver usageSourceResolver,
    ITripVaultResolutionService tripVaultResolutionService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITokenConsumingRequest
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);
        if (!IsSuccessResponse(response))
        {
            return response;
        }

        var user = currentUserContext.User;
        if (user is null)
        {
            return response;
        }

        var requestPayload = ResolveRequestPayload(request);
        var noTraceRequested = RequestPrivacyMode.IsAnonymous(requestPayload);
        if (noTraceRequested && PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(user))
        {
            return response;
        }

        var tokenCost = await serviceTokenCostRepository.GetTokenCostAsync(request.ServiceType, cancellationToken);
        if (!tokenCost.HasValue)
        {
            return ResultResponseFactory<TResponse>.CreateFailure(
                Errors.InternalServerError with
                {
                    Reason = $"Token cost is not configured for service type '{request.ServiceType.Name}'."
                });
        }

        var tripVaultId = await tripVaultResolutionService.ResolveTripVaultIdAsync(
            user.Id,
            (request as ITripVaultQueryRequest)?.TripVaultName,
            cancellationToken);
        var sourceType = usageSourceResolver.ResolveCurrentSource();

        await usageEventWriter.WriteAsync(
            user.Id,
            request.ServiceType,
            tokenCost.Value,
            sourceType,
            DateTime.UtcNow,
            tripVaultId,
            cancellationToken);

        return response;
    }

    private static object? ResolveRequestPayload(TRequest request)
    {
        if (request is ITripVaultQueryRequest tripVaultQueryRequest)
        {
            return tripVaultQueryRequest.GetTripVaultPayload();
        }

        var requestProperty = typeof(TRequest).GetProperty("Request");
        return requestProperty is { CanRead: true }
            ? requestProperty.GetValue(request)
            : null;
    }

    private static bool IsSuccessResponse(TResponse response)
    {
        if (response is Result result)
        {
            return result.IsSuccess;
        }

        return true;
    }
}
