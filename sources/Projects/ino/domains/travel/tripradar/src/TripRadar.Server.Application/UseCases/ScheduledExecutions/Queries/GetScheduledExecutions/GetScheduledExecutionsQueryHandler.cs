using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Queries.GetScheduledExecutions;

public sealed class GetScheduledExecutionsQueryHandler(
    IScheduledExecutionDetailsQueryService scheduledExecutionDetailsQueryService,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<GetScheduledExecutionsQuery, Result<IReadOnlyList<ScheduledExecutionDetails>>>
{
    public async Task<Result<IReadOnlyList<ScheduledExecutionDetails>>> Handle(GetScheduledExecutionsQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        var scheduledExecutions = await scheduledExecutionDetailsQueryService.GetByUserIdAsync(user.Id, cancellationToken);
        return Result.Success(scheduledExecutions);
    }
}
