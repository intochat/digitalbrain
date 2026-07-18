using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IUserLimitService
{
    Task<Result<User>> VerifyLimitEligibilityAsync(string username, ServiceType serviceType, CancellationToken cancellationToken = default);

    Task<Result<User>> VerifyLimitEligibilityAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default);

    Task<Result<TokenConsumptionTicket>> PrepareTokenConsumptionAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default);

    Task<Result> CommitTokenConsumptionAsync(User user, TokenConsumptionTicket ticket);

    Task<Result> RollbackTokenConsumptionAsync(User user, TokenConsumptionTicket ticket, CancellationToken cancellationToken = default);
}
