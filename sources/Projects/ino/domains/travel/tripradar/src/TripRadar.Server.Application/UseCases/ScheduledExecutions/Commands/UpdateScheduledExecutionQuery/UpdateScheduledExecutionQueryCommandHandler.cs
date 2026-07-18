using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionQuery;

public sealed class UpdateScheduledExecutionQueryCommandHandler(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    ICurrentUserContext currentUserContext,
    IScheduledExecutionQueryUpdater scheduledExecutionQueryUpdater)
    : IRequestHandler<UpdateScheduledExecutionQueryCommand, Result>
{
    public async Task<Result> Handle(UpdateScheduledExecutionQueryCommand request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        var user = currentUserContext.GetRequiredUser();
        var scheduledExecution = await scheduledExecutionRepository.GetByUniqueIdAsync(
            request.ScheduledExecutionUniqueId,
            cancellationToken);

        if (scheduledExecution is null)
        {
            return Result.Failure(Errors.ScheduledExecutionNotFound);
        }

        if (scheduledExecution.UserId != user.Id)
        {
            return Result.Failure(Errors.UnauthorizedAccess);
        }

        var result = await scheduledExecutionQueryUpdater.UpdateAsync(scheduledExecution, request, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
