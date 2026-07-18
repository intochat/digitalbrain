namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object? id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateRefreshTokenAsync(T user, CancellationToken cancellationToken = default);
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
}
