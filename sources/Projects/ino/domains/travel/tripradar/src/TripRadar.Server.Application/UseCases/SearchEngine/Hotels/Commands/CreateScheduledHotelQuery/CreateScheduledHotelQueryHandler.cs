using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Constants.ScheduledExecutions;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Commands.CreateScheduledHotelQuery;

public class CreateScheduledHotelQueryHandler(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    IRecurringJobService recurringJobService,
    ISubscriptionPolicy subscriptionPolicy,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<CreateScheduledHotelQueryCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateScheduledHotelQueryCommand request,
        CancellationToken cancellationToken)
    {
        var scheduledExecutionId = Guid.Empty;
        var recurringJobScheduled = false;

        try
        {
            await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

            var user = currentUserContext.GetRequiredUser();

            if (!subscriptionPolicy.IsEligibleForScheduledExecutions(user))
            {
                return Result.Failure<Guid>(Errors.InsufficientSubscriptionTier);
            }

            var scheduledExecution = new ScheduledExecution(user.Id, ScheduledExecutionConstants.ScheduledHotel, request.NextExecutionTime!.Value, request.Schedule!);
            scheduledExecutionId = scheduledExecution.UniqueId;
            await scheduledExecutionRepository.CreateAsync(scheduledExecution, cancellationToken);

            var scheduledHotelQuery = new ScheduledHotelQuery(
                request.Location,
                scheduledExecution.Id,
                user.Id,
                request.CheckInDate,
                request.CheckOutDate,
                request.AdditionalParametersJson.SerializeParameters(),
                request.SelectedColumns);

            await unitOfWork.ScheduledHotelQueryRepository.CreateAsync(scheduledHotelQuery, cancellationToken);

            recurringJobService.ScheduleRecurringExecution(scheduledExecution.UniqueId, scheduledExecution.Schedule, user.Profile.TimezoneCode, cancellationToken);
            recurringJobScheduled = true;
            await scope.CommitAsync(cancellationToken);

            return Result.Success(scheduledExecution.UniqueId);
        }
        catch (Exception)
        {
            if (recurringJobScheduled && scheduledExecutionId != Guid.Empty)
            {
                try
                {
                    recurringJobService.DeleteRecurringExecution(scheduledExecutionId);
                }
                catch
                {
                    // ignored
                }
            }

            throw;
        }
    }
}
