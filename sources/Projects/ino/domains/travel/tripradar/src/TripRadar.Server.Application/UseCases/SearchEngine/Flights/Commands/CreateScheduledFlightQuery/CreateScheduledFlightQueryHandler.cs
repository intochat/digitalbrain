using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Constants.ScheduledExecutions;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Exceptions;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Commands.CreateScheduledFlightQuery;

public class CreateScheduledFlightQueryHandler(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    IRecurringJobService recurringJobService,
    ISubscriptionPolicy subscriptionPolicy,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<CreateScheduledFlightQueryCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateScheduledFlightQueryCommand request,
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

            var departureAirport = await unitOfWork.AirportRepository.GetByCodeAsync(request.DepartureAirportCode, cancellationToken) ??
                                   throw new ObjectNotFoundException($"{request.DepartureAirportCode} - {Errors.AirportCodeNotFound.Reason}");

            var destinationAirport = await unitOfWork.AirportRepository.GetByCodeAsync(request.DestinationAirportCode, cancellationToken) ??
                                     throw new ObjectNotFoundException($"{request.DestinationAirportCode} - {Errors.AirportCodeNotFound.Reason}");

            var scheduledExecution = new ScheduledExecution(user.Id, ScheduledExecutionConstants.ScheduledFlight, request.NextExecutionTime!.Value, request.Schedule!);
            scheduledExecutionId = scheduledExecution.UniqueId;

            await scheduledExecutionRepository.CreateAsync(scheduledExecution, cancellationToken);

            var scheduledFlightQuery = new ScheduledFlightQuery(
                departureAirport.Id,
                destinationAirport.Id,
                scheduledExecution.Id,
                user.Id,
                request.DepartureDate,
                request.ReturnDate,
                request.AdditionalParametersJson.SerializeParameters(),
                request.SelectedColumns);

            await unitOfWork.ScheduledFlightQueryRepository.CreateAsync(scheduledFlightQuery, cancellationToken);

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


