using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Comms.Core.Errors;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Infrastructure.Services.Providers;

public abstract class BaseProviderService<TService>(
    ICacheService cacheService,
    IDistributedLockService distributedLockService,
    ILogger<TService> logger)
{
    private static readonly TimeSpan _defaultLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _defaultLockWaitTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    protected abstract string ProviderName { get; }

    protected Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse, TExternalResponse>(
        TRequest request,
        Func<TRequest, CancellationToken, Task<TExternalResponse?>> serviceCall,
        Func<TExternalResponse, TResponse?> responseMapper,
        CancellationToken cancellationToken = default,
        Error? notFoundError = null,
        Error? mappingError = null,
        Func<Exception, Error?>? exceptionMapper = null)
    {
        return ExecuteWithCachingAsync(
            request,
            async (req, token) =>
            {
                var externalResult = await serviceCall(req, token);
                if (externalResult == null)
                {
                    return Result.Failure<TResponse>(notFoundError ?? Errors.InternalServerError);
                }

                var mappedResult = responseMapper(externalResult);
                return mappedResult == null
                    ? Result.Failure<TResponse>(mappingError ?? Errors.InternalServerError)
                    : Result.Success(mappedResult);
            },
            cancellationToken,
            exceptionMapper);
    }

    private async Task<Result<TResponse>> ExecuteWithCachingAsync<TRequest, TResponse>(
        TRequest request,
        Func<TRequest, CancellationToken, Task<Result<TResponse>>> action,
        CancellationToken cancellationToken,
        Func<Exception, Error?>? exceptionMapper)
    {
        var cacheKey = GenerateCacheKey(request);
        var cachedData = await cacheService.GetByKeyAsync<TResponse>(cacheKey);

        if (cachedData != null)
        {
            return Result.Success(cachedData);
        }

        var lockKey = $"cache:{cacheKey}";
        await using var lockHandle = await distributedLockService.TryAcquireLockAsync(lockKey, _defaultLockTimeout, _defaultLockWaitTimeout, cancellationToken);
        if (lockHandle == null)
        {
            cachedData = await cacheService.GetByKeyAsync<TResponse>(cacheKey);
            if (cachedData != null)
            {
                return Result.Success(cachedData);
            }

            logger.LogWarning("Failed to acquire lock for cache entry {CacheKeyId}, returning error", ToSafeCacheKeyId(cacheKey));
            return Result.Failure<TResponse>(Errors.ServiceUnavailable);
        }

        cachedData = await cacheService.GetByKeyAsync<TResponse>(cacheKey);
        if (cachedData != null)
        {
            return Result.Success(cachedData);
        }

        try
        {
            var result = await action(request, cancellationToken);
            if (result.IsSuccess)
            {
                await cacheService.TrySetAsync(cacheKey, result.Value);
            }

            return result;
        }
        catch (Exception exception) when (exceptionMapper is not null)
        {
            var handledError = exceptionMapper(exception);
            if (handledError is null)
            {
                throw;
            }

            logger.LogError(exception, "Error processing {ProviderName} {RequestType} request", ProviderName, typeof(TRequest).Name);
            return Result.Failure<TResponse>(handledError);
        }
    }

    protected virtual string GenerateCacheKey<TRequest>(TRequest request)
    {
        if (request is ICacheKeyProvider cacheKeyProvider)
        {
            return $"{ProviderName.ToLowerInvariant()}&{cacheKeyProvider.GenerateCacheKey()}";
        }

        var keyParts = new List<string> { ProviderName.ToLowerInvariant() };
        var requestType = typeof(TRequest);
        var properties = _propertyCache.GetOrAdd(requestType, static type =>
            type.GetProperties()
                .OrderBy(property => property.Name)
                .ToArray());

        foreach (var property in properties)
        {
            var value = property.GetValue(request);
            if (value == null)
            {
                continue;
            }

            var encodedKey = Uri.EscapeDataString(property.Name.ToLowerInvariant());
            var encodedValue = Uri.EscapeDataString(FormatCacheValue(value));
            keyParts.Add($"{encodedKey}={encodedValue}");
        }

        return string.Join("&", keyParts);
    }

    private static string FormatCacheValue(object value) =>
        value switch
        {
            string stringValue => stringValue,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static string ToSafeCacheKeyId(string cacheKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }
}
