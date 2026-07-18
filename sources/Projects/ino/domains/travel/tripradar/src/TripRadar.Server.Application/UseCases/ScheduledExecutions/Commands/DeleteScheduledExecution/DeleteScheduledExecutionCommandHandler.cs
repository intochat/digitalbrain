using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.DeleteScheduledExecution;

public sealed class DeleteScheduledExecutionCommandHandler(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    IRecurringJobService recurringJobService,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<DeleteScheduledExecutionCommand, Result>
{
    public async Task<Result> Handle(DeleteScheduledExecutionCommand request, CancellationToken cancellationToken)
    {
        var scheduledExecutionId = request.ScheduledExecutionUniqueId;
        string? schedule = null;
        string? userTimezone = null;
        var isActive = false;
        var recurringJobDeleted = false;

        try
        {
            await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

            var user = currentUserContext.GetRequiredUser();
            userTimezone = user.Profile.TimezoneCode;
            var scheduledExecution = await scheduledExecutionRepository.GetByUniqueIdAsync(request.ScheduledExecutionUniqueId, cancellationToken);
            if (scheduledExecution is null)
            {
                return Result.Failure(Errors.ScheduledExecutionNotFound);
            }

            if (scheduledExecution.UserId != user.Id)
            {
                return Result.Failure(Errors.UnauthorizedAccess);
            }

            schedule = scheduledExecution.Schedule;
            isActive = scheduledExecution.IsActive;

            recurringJobService.DeleteRecurringExecution(scheduledExecution.UniqueId);
            recurringJobDeleted = true;

            await scheduledExecutionRepository.DeleteByUniqueIdAsync(scheduledExecution.UniqueId, cancellationToken);

            await scope.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception)
        {
            if (recurringJobDeleted && isActive && !string.IsNullOrWhiteSpace(schedule))
            {
                try
                {
                    recurringJobService.ScheduleRecurringExecution(scheduledExecutionId, schedule, userTimezone, CancellationToken.None);
                }
                catch
                {
                    // Best-effort compensation; preserve original exception.
                }
            }

            throw;
        }
    }
}



