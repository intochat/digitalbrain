using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Services.UserLimits;

namespace TripRadar.Server.Infrastructure.Services;

public class UserLimitService(
    UserLimitUserLookup userLookup,
    UserLimitDecisionService decisionService,
    UserTokenReservationService reservationService,
    UserTokenCommitService tokenCommitService,
    ILogger<UserLimitService> logger)
    : IUserLimitService
{
    public async Task<Result<User>> VerifyLimitEligibilityAsync(string username, ServiceType serviceType, CancellationToken cancellationToken = default)
    {
        var userResult = await userLookup.GetByUsernameAsync(username, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result.Failure<User>(userResult.Error);
        }

        return await VerifyLimitEligibilityAsync(userResult.Value!, serviceType, cancellationToken);
    }

    public async Task<Result<User>> VerifyLimitEligibilityAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default)
    {
        var decisionResult = await decisionService.ResolveAsync(user, serviceType, cancellationToken);
        if (decisionResult.IsFailure)
        {
            return Result.Failure<User>(decisionResult.Error);
        }

        return decisionResult.Value!.IsAllowed
            ? Result.Success(user)
            : Result.Failure<User>(UserLimitDecisionService.BuildInsufficientTokensError(decisionResult.Value));
    }

    public Task<Result<TokenConsumptionTicket>> PrepareTokenConsumptionAsync(User user, ServiceType serviceType, CancellationToken cancellationToken = default) =>
        reservationService.PrepareAsync(user, user.Profile.Username ?? user.Profile.Email, serviceType, cancellationToken);

    public Task<Result> CommitTokenConsumptionAsync(User user, TokenConsumptionTicket ticket)
    {
        if (!string.Equals(user.Profile.Username, ticket.Username, StringComparison.Ordinal))
        {
            logger.LogWarning("Token consumption ticket username mismatch for user {Username}", user.Profile.Username);
        }

        return tokenCommitService.CommitAsync(user, ticket);
    }

    public Task<Result> RollbackTokenConsumptionAsync(User user, TokenConsumptionTicket ticket, CancellationToken cancellationToken = default) =>
        tokenCommitService.RollbackAsync(user, ticket, cancellationToken);
}
