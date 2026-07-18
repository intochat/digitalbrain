using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;

namespace TripRadar.Server.Infrastructure.Repositories;

public class Repository<T>(DbContext context) : IRepository<T> where T : class
{
    public virtual async Task<T?> GetByIdAsync(object? id, CancellationToken cancellationToken = default) => await context.Set<T>().FindAsync([id], cancellationToken);

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) => await context.Set<T>().ToListAsync(cancellationToken);

    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await context.Set<T>().AddAsync(entity, cancellationToken);
        return entity;
    }

    public void Update(T entity) => context.Set<T>().Update(entity);

    public virtual Task UpdateRefreshTokenAsync(T user, CancellationToken cancellationToken = default)
    {
        context.Set<T>().Update(user);
        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        context.Set<T>().Remove(entity);
        return Task.CompletedTask;
    }
}
