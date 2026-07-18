using MediatR;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Queries.GetScheduledExecutionSearchTypes;

public sealed class GetScheduledExecutionSearchTypesQueryHandler : IRequestHandler<GetScheduledExecutionSearchTypesQuery, Result<IReadOnlyList<string>>>
{
    public Task<Result<IReadOnlyList<string>>> Handle(GetScheduledExecutionSearchTypesQuery request, CancellationToken cancellationToken)
    {
        var searchTypes = Enumeration.GetAll<ScheduledExecutionSearchType>()
            .OrderBy(searchType => searchType.Id)
            .Select(searchType => searchType.Name)
            .ToList();

        return Task.FromResult(Result.Success<IReadOnlyList<string>>(searchTypes));
    }
}
