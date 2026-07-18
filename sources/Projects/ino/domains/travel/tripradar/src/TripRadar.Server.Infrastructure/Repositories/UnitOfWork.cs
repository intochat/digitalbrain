using System.Transactions;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Events;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public sealed class UnitOfWork(
    TripRadarDbContext context,
    IAirportRepository airportRepository,
    ILocationRepository locationRepository,
    IScheduledFlightQueryRepository scheduledFlightQueryRepository,
    IUserRepository userRepository,
    IScheduledHotelQueryRepository scheduledHotelQueryRepository,
    IScheduledEventQueryRepository scheduledEventQueryRepository,
    IScheduledLocalPlacesQueryRepository scheduledLocalPlacesQueryRepository,
    IPriceRepository priceRepository,
    IPromoCodeRepository promoCodeRepository,
    ITripVaultRepository tripVaultRepository,
    IDomainEventDispatcher domainEventDispatcher) : IUnitOfWork
{
    private bool _disposed;

    public IScheduledFlightQueryRepository ScheduledFlightQueryRepository { get; } = scheduledFlightQueryRepository;

    public IScheduledEventQueryRepository ScheduledEventQueryRepository { get; } = scheduledEventQueryRepository;

    public IScheduledHotelQueryRepository ScheduledHotelQueryRepository { get; } = scheduledHotelQueryRepository;

    public IScheduledLocalPlacesQueryRepository ScheduledLocalPlacesQueryRepository { get; } = scheduledLocalPlacesQueryRepository;

    public IAirportRepository AirportRepository { get; } = airportRepository;

    public ILocationRepository LocationRepository { get; } = locationRepository;

    public IUserRepository UserRepository { get; } = userRepository;

    public IPriceRepository PriceRepository { get; } = priceRepository;

    public IPromoCodeRepository PromoCodeRepository { get; } = promoCodeRepository;

    public ITripVaultRepository TripVaultRepository { get; } = tripVaultRepository;

    public async Task<UnitOfWorkTransactionScope> StartScopeAsync(
        TransactionScopeOption scopeOption = TransactionScopeOption.Required,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        if (scopeOption == TransactionScopeOption.Suppress)
        {
            return new UnitOfWorkTransactionScope(
                commitAsync: SaveChangesIfNeededAsync,
                disposeAsync: () => ValueTask.CompletedTask);
        }

        var currentTransaction = context.Database.CurrentTransaction;
        if (currentTransaction is not null)
        {
            if (scopeOption == TransactionScopeOption.Required)
            {
                return new UnitOfWorkTransactionScope(
                    commitAsync: SaveChangesIfNeededAsync,
                    disposeAsync: () => ValueTask.CompletedTask);
            }

            if (scopeOption == TransactionScopeOption.RequiresNew)
            {
                var savepointName = $"uow_{Guid.NewGuid():N}";
                await currentTransaction.CreateSavepointAsync(savepointName, cancellationToken);

                var committed = false;
                return new UnitOfWorkTransactionScope(
                    commitAsync: async ct =>
                    {
                        await SaveChangesIfNeededAsync(ct);
                        await currentTransaction.ReleaseSavepointAsync(savepointName, ct);
                        committed = true;
                    },
                    disposeAsync: async () =>
                    {
                        if (committed)
                        {
                            return;
                        }

                        await currentTransaction.RollbackToSavepointAsync(savepointName, CancellationToken.None);
                        await currentTransaction.ReleaseSavepointAsync(savepointName, CancellationToken.None);
                    });
            }
        }

        var createdTransaction = await context.Database.BeginTransactionAsync(ToDbIsolationLevel(isolationLevel), cancellationToken);
        var isCommitted = false;

        return new UnitOfWorkTransactionScope(
            commitAsync: async ct =>
            {
                await SaveChangesIfNeededAsync(ct);
                await createdTransaction.CommitAsync(ct);
                isCommitted = true;
                await DispatchDomainEventsAsync(ct);
            },
            disposeAsync: async () =>
            {
                if (!isCommitted)
                {
                    await createdTransaction.RollbackAsync(CancellationToken.None);
                }

                await createdTransaction.DisposeAsync();
            });
    }

    private static System.Data.IsolationLevel ToDbIsolationLevel(IsolationLevel isolationLevel)
    {
        return isolationLevel switch
        {
            IsolationLevel.ReadUncommitted => System.Data.IsolationLevel.ReadUncommitted,
            IsolationLevel.ReadCommitted => System.Data.IsolationLevel.ReadCommitted,
            IsolationLevel.RepeatableRead => System.Data.IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable => System.Data.IsolationLevel.Serializable,
            IsolationLevel.Snapshot => System.Data.IsolationLevel.Snapshot,
            _ => System.Data.IsolationLevel.ReadCommitted
        };
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var affectedRows = await context.SaveChangesAsync(cancellationToken);
        if (context.Database.CurrentTransaction is null)
        {
            await DispatchDomainEventsAsync(cancellationToken);
        }

        return affectedRows;
    }

    private async Task SaveChangesIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!context.ChangeTracker.HasChanges())
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);
        if (context.Database.CurrentTransaction is null)
        {
            await DispatchDomainEventsAsync(cancellationToken);
        }
    }

    private Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var domainEvents = context.ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<IHasDomainEvents>()
            .Where(entity => entity.DomainEvents.Count > 0)
            .SelectMany(entity => entity.DequeueDomainEvents())
            .ToArray();

        foreach (var domainEvent in domainEvents)
        {
            domainEventDispatcher.Publish(domainEvent);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        context.Dispose();
        _disposed = true;
    }
}
