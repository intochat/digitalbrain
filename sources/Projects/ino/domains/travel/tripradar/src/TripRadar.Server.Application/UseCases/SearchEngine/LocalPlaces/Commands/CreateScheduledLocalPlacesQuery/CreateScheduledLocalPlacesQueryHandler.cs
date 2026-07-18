using System.Text.Json;
using MediatR;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Constants.ScheduledExecutions;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Exceptions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.SearchEngine.LocalPlaces.Commands.CreateScheduledLocalPlacesQuery;

public class CreateScheduledLocalPlacesQueryHandler(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    IRecurringJobService recurringJobService,
    ISubscriptionPolicy subscriptionPolicy,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<CreateScheduledLocalPlacesQueryCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateScheduledLocalPlacesQueryCommand request, CancellationToken cancellationToken)
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

            var scheduledExecution = new ScheduledExecution(user.Id, ScheduledExecutionConstants.ScheduledLocalPlaces, request.NextExecutionTime!.Value, request.Schedule!);
            scheduledExecutionId = scheduledExecution.UniqueId;

            await scheduledExecutionRepository.CreateAsync(scheduledExecution, cancellationToken);

            var additionalParams = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(request.AdditionalParametersJson))
            {
                try
                {
                    var existingParams = JsonSerializer.Deserialize<Dictionary<string, object>>(request.AdditionalParametersJson);
                    if (existingParams != null)
                    {
                        foreach (var kvp in existingParams)
                        {
                            additionalParams[kvp.Key] = kvp.Value;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    throw new InvalidRequestException($"Invalid AdditionalParametersJson format: {ex.Message}", ex);
                }
            }

            if (!string.IsNullOrEmpty(request.Location))
            {
                additionalParams["location"] = request.Location;
            }

            if (request.Radius.HasValue)
            {
                additionalParams["radius"] = request.Radius.Value;
            }

            var serializedParams = JsonSerializer.Serialize(additionalParams);

            var scheduledLocalPlacesQuery = new ScheduledLocalPlaceQuery(
                request.SearchQuery,
                scheduledExecution.Id,
                user.Id,
                serializedParams,
                request.SelectedColumns);

            await unitOfWork.ScheduledLocalPlacesQueryRepository.CreateAsync(scheduledLocalPlacesQuery, cancellationToken);

            recurringJobService.ScheduleRecurringExecution(scheduledExecution.UniqueId, scheduledExecution.Schedule, user.Profile.TimezoneCode, cancellationToken);
            recurringJobScheduled = true;

            await scope.CommitAsync(cancellationToken);

            return Result.Success(scheduledExecution.UniqueId);
        }
        catch (Exception exception)
        {
            if (recurringJobScheduled && scheduledExecutionId != Guid.Empty)
            {
                try
                {
                    recurringJobService.DeleteRecurringExecution(scheduledExecutionId);
                }
                catch
                {
                }
            }

            var errorMessage = $"Failed to create scheduled local places query: {exception.Message}";
            if (exception.InnerException != null)
            {
                errorMessage += $" Inner exception: {exception.InnerException.Message}";
            }

            throw new InvalidRequestException(errorMessage, exception);
        }
    }
}


