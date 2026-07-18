namespace TripRadar.Server.Application.Contracts.Services;

public interface ICacheService
{
    Task TrySetAsync<T>(string key, T value, int? hours = null);

    Task<T?> GetByKeyAsync<T>(string key);

    Task RemoveAsync(string key);
}
