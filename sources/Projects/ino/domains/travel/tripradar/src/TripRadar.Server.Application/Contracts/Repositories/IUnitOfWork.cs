using System.Transactions;

namespace TripRadar.Server.Application.Contracts.Repositories;

public interface IUnitOfWork : IDisposable
{
    IScheduledFlightQueryRepository ScheduledFlightQueryRepository { get; }

    IScheduledHotelQueryRepository ScheduledHotelQueryRepository { get; }

    IScheduledEventQueryRepository ScheduledEventQueryRepository { get; }

    IScheduledLocalPlacesQueryRepository ScheduledLocalPlacesQueryRepository { get; }

    IAirportRepository AirportRepository { get; }

    ILocationRepository LocationRepository { get; }

    IUserRepository UserRepository { get; }

    IPriceRepository PriceRepository { get; }

    IPromoCodeRepository PromoCodeRepository { get; }

    ITripVaultRepository TripVaultRepository { get; }

    Task<UnitOfWorkTransactionScope> StartScopeAsync(
        TransactionScopeOption scopeOption = TransactionScopeOption.Required,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
