using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Events;
using AppErrors = TripRadar.Server.Application.ApplicationErrors.Errors;

namespace TripRadar.Server.Infrastructure.Services.UserLimits;

public sealed class UserTokenCommitService(
    UserLimitDecisionService decisionService,
    IUserMonthlyTokenCountRepository userMonthlyTokenCountRepository,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<UserTokenCommitService> logger)
{
    public Task<Result> CommitAsync(User user, TokenConsumptionTicket ticket)
    {
        if (ticket.Type == TokenConsumptionType.Tier)
        {
            return Task.FromResult(Result.Success());
        }

        if (ticket.Type == TokenConsumptionType.Overage && ticket.TokenCost is not null)
        {
            user.RecordTokenConsumption(ticket.ServiceType, ticket.Type, ticket.TokenCost.Value);
            foreach (var domainEvent in user.DequeueDomainEvents())
            {
                domainEventDispatcher.Publish(domainEvent);
            }

            return Task.FromResult(Result.Success());
        }

        logger.LogError("Invalid token consumption ticket for user {Username}", ticket.Username);
        return Task.FromResult(Result.Failure(AppErrors.InternalServerError));
    }

    public async Task<Result> RollbackAsync(User user, TokenConsumptionTicket ticket, CancellationToken cancellationToken)
    {
        if (ticket.Type != TokenConsumptionType.Tier)
        {
            return Result.Success();
        }

        var tokenCostResult = await decisionService.GetTokenCostAsync(ticket.ServiceType, cancellationToken);
        if (tokenCostResult.IsFailure)
        {
            return Result.Failure(tokenCostResult.Error);
        }

        var refunded = await userMonthlyTokenCountRepository.TryRefundTokensAsync(user, tokenCostResult.Value!, cancellationToken);
        if (refunded)
        {
            return Result.Success();
        }

        logger.LogWarning("Failed to rollback token consumption for user {Username}", user.Profile.Username);
        return Result.Failure(AppErrors.InternalServerError);
    }
}
