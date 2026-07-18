using System.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Providers;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Factories;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services.Providers.SerpApi;

public class SerpApiProviderService(
    ICacheService cacheService,
    IDistributedLockService distributedLockService,
    IOptions<ResiliencePolicySettings> resiliencePolicySettings,
    ISerpApiProvider serpApiProvider,
    ILogger<SerpApiProviderService> logger)
    : BaseProviderService<SerpApiProviderService>(cacheService, distributedLockService, logger), ISerpApiProviderService
{
    private readonly IAsyncPolicy<string?> _resiliencePolicy = ResiliencePolicyFactory.CreateStandardPolicy<string?>(ProvidersType.SerpApi.Name, logger, resiliencePolicySettings.Value);

    protected override string ProviderName => ProvidersType.SerpApi.Name;

    public Task<Result<TResponse>> SearchAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : ISerpApiRequest =>
        ExecuteAsync<TRequest, TResponse, string?>(
            request,
            ExecuteProviderRequestAsync,
            SerpApiResponseDeserializer.Deserialize<TResponse>,
            ct,
            notFoundError: Errors.SerpApiRequestFailed,
            mappingError: Errors.DeserializationFailed,
            exceptionMapper: SerpApiErrorMapper.Map);

    protected override string GenerateCacheKey<TRequest>(TRequest request) =>
        request is ISerpApiRequest serpApiRequest
            ? SerpApiCacheKeyBuilder.Build(ProviderName, serpApiRequest.GetQueryParams())
            : base.GenerateCacheKey(request);

    private Task<string?> ExecuteProviderRequestAsync<TRequest>(TRequest request, CancellationToken cancellationToken)
        where TRequest : ISerpApiRequest =>
        _resiliencePolicy.ExecuteAsync(
            token => FetchResponseJsonAsync(request.GetQueryParams(), token),
            cancellationToken);

    private async Task<string?> FetchResponseJsonAsync(Hashtable queryParams, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await serpApiProvider.FindAsync(queryParams, cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }
}
