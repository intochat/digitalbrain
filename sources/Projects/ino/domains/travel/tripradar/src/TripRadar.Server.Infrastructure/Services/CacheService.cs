using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Settings;

namespace TripRadar.Server.Infrastructure.Services;

public class CacheService : ICacheService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<CacheService> _logger;
    private readonly DistributedCacheEntryOptions _defaultCacheOptions;
    private readonly bool _throwOnCacheErrors;

    public CacheService(
        IDistributedCache distributedCache,
        IOptions<CachingSettings> options,
        IHostEnvironment environment,
        ILogger<CacheService> logger)
    {
        _distributedCache = distributedCache;
        _logger = logger;
        _throwOnCacheErrors = environment.IsDevelopment();
        var caching = options.Value;

        var expirationHours = caching.DefaultExpirationHours <= 0 ? 1 : caching.DefaultExpirationHours;
        _defaultCacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(expirationHours)
        };
    }

    public async Task TrySetAsync<T>(string key, T data, int? hours = null)
    {
        try
        {
            DistributedCacheEntryOptions cacheOptions;

            if (hours.HasValue)
            {
                var expiration = hours.Value <= 0 ? 1 : hours.Value;
                cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(expiration)
                };
            }
            else
            {
                cacheOptions = _defaultCacheOptions;
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, _jsonOptions);
            await _distributedCache.SetAsync(key, bytes, cacheOptions);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set cache key {Key}: {Message}", key, e.Message);
            if (_throwOnCacheErrors)
                throw;
        }
    }

    public async Task<T?> GetByKeyAsync<T>(string key)
    {
        try
        {
            var data = await _distributedCache.GetAsync(key);
            if (data is null || data.Length == 0)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(data, _jsonOptions);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get cache key {Key}: {Message}", key, e.Message);
            if (_throwOnCacheErrors)
                throw;
            return default;
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _distributedCache.RemoveAsync(key);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to remove cache key {Key}: {Message}", key, e.Message);
            if (_throwOnCacheErrors)
                throw;
        }
    }
}
