using MediatR;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Queries.GetScheduledExecutionSearchTypes;

public sealed record GetScheduledExecutionSearchTypesQuery : IRequest<Result<IReadOnlyList<string>>>;
