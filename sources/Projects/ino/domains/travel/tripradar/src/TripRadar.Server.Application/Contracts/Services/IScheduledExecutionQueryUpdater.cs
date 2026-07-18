using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionQuery;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IScheduledExecutionQueryUpdater
{
    Task<Result> UpdateAsync(ScheduledExecution scheduledExecution, UpdateScheduledExecutionQueryCommand request, CancellationToken cancellationToken);
}
