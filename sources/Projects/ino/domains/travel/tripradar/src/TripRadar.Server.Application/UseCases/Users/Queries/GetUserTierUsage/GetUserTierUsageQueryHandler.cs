using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetUserTierUsage;

public class GetUserTierUsageQueryHandler(
    ITierLimitService tierLimitService,
    ICurrentUserContext currentUserContext)
    : IRequestHandler<GetUserTierUsageQuery, Result<GetUserTierUsageResponseDTO>>
{
    public async Task<Result<GetUserTierUsageResponseDTO>> Handle(GetUserTierUsageQuery request,
        CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();

        var (currentUsage, dailyLimit) = await tierLimitService.GetUserTokenStatusAsync(user, cancellationToken);
        var remainingRequests = Math.Max(0, dailyLimit - currentUsage);
        var usagePercentage = dailyLimit > 0 ? (double)currentUsage / (double)dailyLimit * 100 : 0;

        return Result.Success(new GetUserTierUsageResponseDTO(user.Tier.Name, currentUsage, dailyLimit, remainingRequests, Math.Round(usagePercentage, 2)));
    }
}
