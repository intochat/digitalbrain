using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.Behaviors;

public class QueryHistoryBehavior<TRequest, TResponse>(
    ITripVaultResolutionService tripVaultResolutionService,
    ICurrentUserContext currentUserContext,
    IBackgroundJobService backgroundJobService,
    ILogger<QueryHistoryBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int MaxResultSummaryLength = 50000;

    private static readonly JsonSerializerOptions _summaryJsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions _queryPayloadJsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (request is ITripVaultQueryRequest tripVaultRequest && IsSuccess(response))
        {
            await TryEnqueueQueryHistorySaveAsync(tripVaultRequest, response, cancellationToken);
        }

        return response;
    }

    private async Task TryEnqueueQueryHistorySaveAsync(ITripVaultQueryRequest tripVaultRequest, TResponse response, CancellationToken cancellationToken)
    {
        try
        {
            var requestPayload = tripVaultRequest.GetTripVaultPayload();
            if (RequestPrivacyMode.IsAnonymous(requestPayload))
            {
                return;
            }

            var currentUser = currentUserContext.User;
            if (currentUser is null)
            {
                return;
            }

            var canUseUserVaults = PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(currentUser);
            var tripVaultUniqueId = await tripVaultResolutionService.ResolveTripVaultUniqueIdAsync(
                currentUser.Id,
                canUseUserVaults ? tripVaultRequest.TripVaultName : null,
                createDefaultIfMissing: true,
                cancellationToken);

            if (!tripVaultUniqueId.HasValue)
            {
                return;
            }

            var queryParametersJson = JsonSerializer.Serialize(requestPayload, _queryPayloadJsonSerializerOptions);

            backgroundJobService.EnqueueTripVaultQueryHistorySave(
                tripVaultUniqueId.Value,
                tripVaultRequest.ServiceType.Id,
                queryParametersJson,
                TryBuildResultSummary(response));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Skipping trip history save for request {RequestType} due to vault resolution failure.", typeof(TRequest).Name);
        }
    }

    private static string? TryBuildResultSummary(TResponse response)
    {
        var responseType = typeof(TResponse);
        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(Result<>))
        {
            return null;
        }

        try
        {
            var value = responseType.GetProperty("Value")?.GetValue(response);
            if (value is null)
            {
                return null;
            }

            var serialized = JsonSerializer.Serialize(value, _summaryJsonSerializerOptions);
            if (serialized.Length <= MaxResultSummaryLength)
            {
                return serialized;
            }

            return JsonSerializer.Serialize(new
                {
                    truncated = true,
                    originalLength = serialized.Length,
                    preview = serialized[..MaxResultSummaryLength]
                },
                _summaryJsonSerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSuccess(TResponse response) => response is Result { IsSuccess: true };
}
