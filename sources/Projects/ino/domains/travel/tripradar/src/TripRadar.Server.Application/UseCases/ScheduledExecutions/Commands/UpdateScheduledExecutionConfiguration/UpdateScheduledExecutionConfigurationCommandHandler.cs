using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Application.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionConfiguration;

public class UpdateScheduledExecutionConfigurationCommandHandler(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    IRecurringJobService recurringJobService,
    ICurrentUserContext currentUserContext,
    IScheduledExecutionValidityService scheduledExecutionValidityService)
    : IRequestHandler<UpdateScheduledExecutionConfigurationCommand, Result>
{
    public async Task<Result> Handle(UpdateScheduledExecutionConfigurationCommand request, CancellationToken cancellationToken)
    {
        var scheduledExecutionId = request.ScheduledExecutionUniqueId;
        string? previousSchedule = null;
        string? userTimezone = null;
        var previousIsActive = false;
        var jobMutation = RecurringJobMutationType.None;

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

            previousSchedule = scheduledExecution.Schedule;
            previousIsActive = scheduledExecution.IsActive;

            var schedule = string.IsNullOrWhiteSpace(request.Schedule) ? scheduledExecution.Schedule : request.Schedule.Trim();
            var nextExecutionTime = request.NextExecutionTime ?? scheduledExecution.NextExecutionTime;

            if (request.IsActive)
            {
                var searchType = scheduledExecution.GetSearchType();
                if (searchType is null)
                {
                    return Result.Failure(Errors.SearchTypeNotFound);
                }

                var startDate = await ResolveStartDateAsync(scheduledExecution, searchType, cancellationToken);
                if (!scheduledExecutionValidityService.IsExecutableAtNextRun(searchType, nextExecutionTime, startDate))
                {
                    return Result.Failure(Errors.InvalidScheduledExecutionWindow);
                }
            }

            await scheduledExecutionRepository.UpdateConfigurationAsync(scheduledExecution.UniqueId, request.IsActive, schedule, nextExecutionTime, cancellationToken);

            if (request.IsActive)
            {
                recurringJobService.ScheduleRecurringExecution(
                    scheduledExecution.UniqueId,
                    schedule,
                    userTimezone,
                    cancellationToken);
                jobMutation = RecurringJobMutationType.Scheduled;
            }
            else
            {
                recurringJobService.DeleteRecurringExecution(scheduledExecution.UniqueId);
                jobMutation = RecurringJobMutationType.Deleted;
            }

            await scope.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception)
        {
            try
            {
                switch (jobMutation)
                {
                    case RecurringJobMutationType.Scheduled:
                        if (previousIsActive && !string.IsNullOrWhiteSpace(previousSchedule))
                        {
                            recurringJobService.ScheduleRecurringExecution(
                                scheduledExecutionId,
                                previousSchedule,
                                userTimezone,
                                CancellationToken.None);
                        }
                        else
                        {
                            recurringJobService.DeleteRecurringExecution(scheduledExecutionId);
                        }

                        break;
                    case RecurringJobMutationType.Deleted when previousIsActive && !string.IsNullOrWhiteSpace(previousSchedule):
                        recurringJobService.ScheduleRecurringExecution(
                            scheduledExecutionId,
                            previousSchedule,
                            userTimezone,
                            CancellationToken.None);
                        break;
                    case RecurringJobMutationType.None:
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown recurring job mutation type: {jobMutation}.");
                }
            }
            catch
            {
                // Best-effort compensation; preserve original exception.
            }

            throw;
        }
    }

    private async Task<DateTime?> ResolveStartDateAsync(
        ScheduledExecution scheduledExecution,
        ScheduledExecutionSearchType searchType,
        CancellationToken cancellationToken)
    {
        if (Equals(searchType, ScheduledExecutionSearchType.Flights))
        {
            var flightQuery = await unitOfWork.ScheduledFlightQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken);
            return flightQuery?.DepartureDate;
        }

        if (Equals(searchType, ScheduledExecutionSearchType.Hotels))
        {
            var hotelQuery = await unitOfWork.ScheduledHotelQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken);
            return hotelQuery?.CheckInDate;
        }

        if (Equals(searchType, ScheduledExecutionSearchType.Events))
        {
            var eventQuery = await unitOfWork.ScheduledEventQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken);
            return scheduledExecutionValidityService.ExtractEventStartDate(eventQuery?.AdditionalParameters);
        }

        return null;
    }
}
