using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Infrastructure.Contracts.Handlers;

public interface ISearchTypeHandler
{
    Task HandleSearchAsync(ScheduledExecution scheduledExecution, ISerpApiRequest request, CancellationToken cancellation);
}
