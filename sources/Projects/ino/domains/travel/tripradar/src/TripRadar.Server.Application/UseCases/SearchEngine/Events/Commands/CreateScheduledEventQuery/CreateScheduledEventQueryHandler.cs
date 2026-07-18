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

namespace TripRadar.Server.Application.UseCases.SearchEngine.Events.Commands.CreateScheduledEventQuery;

public class CreateScheduledEventQueryHandler(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    IRecurringJobService recurringJobService,
    ISubscriptionPolicy subscriptionPolicy,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<CreateScheduledEventQueryCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateScheduledEventQueryCommand request, CancellationToken cancellationToken)
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

            var scheduledExecution = new ScheduledExecution(user.Id, ScheduledExecutionConstants.ScheduledEvent, request.NextExecutionTime!.Value, request.Schedule!);
            scheduledExecutionId = scheduledExecution.UniqueId;

            await scheduledExecutionRepository.CreateAsync(scheduledExecution, cancellationToken);

            var scheduledEventQuery = new ScheduledEventQuery(
                request.SearchQuery,
                scheduledExecution.Id,
                user.Id,
                request.AdditionalParametersJson.SerializeParameters(),
                request.SelectedColumns);

            await unitOfWork.ScheduledEventQueryRepository.CreateAsync(scheduledEventQuery, cancellationToken);

            recurringJobService.ScheduleRecurringExecution(scheduledExecution.UniqueId, scheduledExecution.Schedule, user.Profile.TimezoneCode, cancellationToken);
            recurringJobScheduled = true;

            await scope.CommitAsync(cancellationToken);

            return Result.Success(scheduledExecution.UniqueId);
        }
        catch (Exception)
        {
            if (!recurringJobScheduled || scheduledExecutionId == Guid.Empty)
            {
                throw;
            }

            try
            {
                recurringJobService.DeleteRecurringExecution(scheduledExecutionId);
            }
            catch
            {
            }

            throw;
        }
    }
}


