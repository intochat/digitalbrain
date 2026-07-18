namespace TripRadar.Server.Application.Contracts.Services.Providers;

/// <summary>
/// Interface for request DTOs that can generate their own cache keys efficiently.
/// Implementing this interface avoids reflection-based cache key generation.
/// </summary>
public interface ICacheKeyProvider
{
    /// <summary>
    /// Generates a cache key string for this request.
    /// The key should be unique for the combination of request parameters.
    /// </summary>
    string GenerateCacheKey();
}
