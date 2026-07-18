using System.Globalization;
using Hangfire;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.Extensions;
using TripRadar.Server.Comms.Core.Exceptions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Mappings;
using TripRadar.Server.Infrastructure.Contracts.Scheduled;

namespace TripRadar.Server.Infrastructure.Services.Strategies;

public class ScheduledQueryExecutionService(
    IUnitOfWork unitOfWork,
    IScheduledExecutionRepository scheduledExecutionRepository,
    IServiceTokenCostRepository serviceTokenCostRepository,
    ILogger<ScheduledQueryExecutionService> logger,
    IUserLimitService userLimitService,
    IUsageEventWriter usageEventWriter,
    IScheduledJobManager scheduledJobManager,
    ICronExpressionService cronExpressionService,
    IScheduledExecutionValidityService scheduledExecutionValidityService,
    IEnumerable<IScheduledExecutionStrategy> strategies) : IScheduledQueryExecutionService
{
    private const string JobIdFormat = "scheduled-execution-{0}";

    private sealed record Execution(
        Domain.Entities.ScheduledExecution ScheduledExecution,
        ScheduledExecutionSearchType SearchType,
        TokenConsumptionTicket Ticket,
        User User);

    [AutomaticRetry(OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task ExecuteQueryAsync(Guid query, CancellationToken cancellationToken = default)
    {
        var preparationResult = await PrepareExecutionAsync(query, cancellationToken);

        if (preparationResult.IsFailure)
            return;

        var (scheduledExecution, searchType, ticket, user) = preparationResult.Value!;

        if (!scheduledExecution.IsActive)
        {
            await FinalizeExecutionAsync(scheduledExecution, user, ticket, executed: false, cancellationToken);
            return;
        }

        if (await DeactivateIfInvalidAsync(scheduledExecution, searchType, cancellationToken))
        {
            await FinalizeExecutionAsync(scheduledExecution, user, ticket, executed: false, cancellationToken);
            return;
        }

        try
        {
            var executed = await ExecuteStrategyAsync(scheduledExecution, searchType, cancellationToken);

            await FinalizeExecutionAsync(scheduledExecution, user, ticket, executed, cancellationToken);
        }
        catch (InvalidRequestException exception)
        {
            await RollbackReservedTokensAsync(user, ticket, query, cancellationToken);
            logger.LogError(exception, "Error executing scheduled query {QueryId}", query);
            throw;
        }
        catch (Exception exception)
        {
            await RollbackReservedTokensAsync(user, ticket, query, cancellationToken);
            logger.LogError(exception, "Unexpected error executing scheduled query {QueryId}", query);
            throw;
        }
    }

    private async Task<Result<Execution>> PrepareExecutionAsync(Guid query, CancellationToken cancellationToken)
    {
        var scheduledExecution = await scheduledExecutionRepository.GetByUniqueIdAsync(query, cancellationToken);
        if (scheduledExecution is null)
            return Result.Failure<Execution>(Errors.ScheduledExecutionNotFound);

        var searchType = scheduledExecution.GetSearchType();
        if (searchType is null)
        {
            logger.LogError("Scheduled execution with unique id - {QueryId} has no search type", query);
            return Result.Failure<Execution>(Errors.SearchTypeNotFound);
        }

        var serviceType = searchType.ToServiceType();
        if (serviceType is null)
             throw new ArgumentException($"Unknown search type: {searchType.Name}");

        var user = await unitOfWork.UserRepository.GetByIdForLimitsAsync(scheduledExecution.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogError("User not found for scheduled execution {QueryId}", query);
            return Result.Failure<Execution>(Errors.UserNotFound);
        }

        scheduledExecution.AttachUser(user);

        var ticketResult = await userLimitService.PrepareTokenConsumptionAsync(
            user,
            serviceType,
            cancellationToken);

        if (ticketResult.IsFailure)
        {
            logger.LogInformation("Scheduled execution with unique id - {QueryId} is not allowed due to token limits", query);
            return Result.Failure<Execution>(ticketResult.Error);
        }

        return Result.Success(new Execution(scheduledExecution, searchType, ticketResult.Value!, user));
    }

    private async Task<bool> ExecuteStrategyAsync(
        Domain.Entities.ScheduledExecution scheduledExecution,
        ScheduledExecutionSearchType searchType,
        CancellationToken cancellationToken)
    {
        var strategy = strategies.FirstOrDefault(s => s.CanHandle(searchType));

        if (strategy is not null)
            return await strategy.ExecuteAsync(scheduledExecution, cancellationToken);

        logger.LogError("No execution strategy found for search type {SearchType}", searchType.Name);
        return false;
    }

    private async Task<bool> DeactivateIfInvalidAsync(
        Domain.Entities.ScheduledExecution scheduledExecution,
        ScheduledExecutionSearchType searchType,
        CancellationToken cancellationToken)
    {
        var (hasLinkedQuery, startDate) = await ResolveExecutionWindowAsync(scheduledExecution.Id, searchType, cancellationToken);
        if (hasLinkedQuery && scheduledExecutionValidityService.IsExecutableAtNextRun(searchType, scheduledExecution.NextExecutionTime, startDate))
        {
            return false;
        }

        if (scheduledExecution.IsActive)
        {
            await scheduledExecutionRepository.UpdateActiveStatusAsync(scheduledExecution.UniqueId, false, cancellationToken);
            scheduledExecution.UpdateActiveStatus(false);
        }

        return true;
    }

    private async Task<(bool HasLinkedQuery, DateTime? StartDate)> ResolveExecutionWindowAsync(
        long scheduledExecutionId,
        ScheduledExecutionSearchType searchType,
        CancellationToken cancellationToken)
    {
        if (Equals(searchType, ScheduledExecutionSearchType.Flights))
        {
            var flightQuery = await unitOfWork.ScheduledFlightQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecutionId, cancellationToken);
            return (flightQuery is not null, flightQuery?.DepartureDate);
        }

        if (Equals(searchType, ScheduledExecutionSearchType.Hotels))
        {
            var hotelQuery = await unitOfWork.ScheduledHotelQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecutionId, cancellationToken);
            return (hotelQuery is not null, hotelQuery?.CheckInDate);
        }

        if (Equals(searchType, ScheduledExecutionSearchType.Events))
        {
            var eventQuery = await unitOfWork.ScheduledEventQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecutionId, cancellationToken);
            return (eventQuery is not null, scheduledExecutionValidityService.ExtractEventStartDate(eventQuery?.AdditionalParameters));
        }

        return (true, null);
    }

    private async Task FinalizeExecutionAsync(
        Domain.Entities.ScheduledExecution scheduledExecution,
        User user,
        TokenConsumptionTicket? ticket,
        bool executed,
        CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);

        if (!scheduledExecution.IsActive)
        {
            RemoveRecurringJob(scheduledExecution.UniqueId);
        }
        else
        {
            var nextOccurrence = cronExpressionService.GetNextOccurrence(scheduledExecution.Schedule);
            if (nextOccurrence.HasValue)
                await scheduledExecutionRepository.UpdateNextExecutionTimeAsync(
                    scheduledExecution.UniqueId,
                    nextOccurrence.Value,
                    cancellationToken);
        }

        if (ticket != null)
        {
            if (!executed)
            {
                var rollbackResult = await userLimitService.RollbackTokenConsumptionAsync(user, ticket, cancellationToken);
                if (rollbackResult.IsFailure)
                    logger.LogError("Failed to rollback token consumption for scheduled execution {QueryId}", scheduledExecution.UniqueId);
            }
            else
            {
                if (Equals(ticket.Type, TokenConsumptionType.Overage))
                {
                    var commitResult = await userLimitService.CommitTokenConsumptionAsync(user, ticket);
                    if (commitResult.IsFailure)
                    {
                        logger.LogError("Failed to commit token consumption for scheduled execution {QueryId}", scheduledExecution.UniqueId);
                        await scope.CommitAsync(cancellationToken);
                        return;
                    }
                }

                var tokenCost = await ResolveTokenCostAsync(ticket, cancellationToken);
                if (!tokenCost.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Token cost is not configured for service type '{ticket.ServiceType.Name}'.");
                }

                await usageEventWriter.WriteAsync(
                    user.Id,
                    ticket.ServiceType,
                    tokenCost.Value,
                    UsageEventSourceType.Scheduled,
                    DateTime.UtcNow,
                    scheduledExecution.TripVaultId,
                    cancellationToken);
            }
        }

        await scope.CommitAsync(cancellationToken);
    }

    private async Task<decimal?> ResolveTokenCostAsync(TokenConsumptionTicket ticket, CancellationToken cancellationToken)
    {
        if (Equals(ticket.Type, TokenConsumptionType.Overage) && ticket.TokenCost.HasValue)
        {
            return ticket.TokenCost.Value;
        }

        return await serviceTokenCostRepository.GetTokenCostAsync(ticket.ServiceType, cancellationToken);
    }

    private async Task RollbackReservedTokensAsync(
        User user,
        TokenConsumptionTicket? ticket,
        Guid query,
        CancellationToken cancellationToken)
    {
        if (ticket is null)
        {
            return;
        }

        var rollbackResult = await userLimitService.RollbackTokenConsumptionAsync(user, ticket, cancellationToken);
        if (rollbackResult.IsFailure)
        {
            logger.LogError("Failed to rollback token consumption after scheduled execution error for query {QueryId}", query);
        }
    }

    private void RemoveRecurringJob(Guid executionId) =>
        scheduledJobManager.RemoveIfExists(string.Format(CultureInfo.InvariantCulture, JobIdFormat, executionId));
}


