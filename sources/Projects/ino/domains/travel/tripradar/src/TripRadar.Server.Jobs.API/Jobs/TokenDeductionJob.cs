using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Jobs.API.Jobs;

public class TokenDeductionJob(
    IUnitOfWork unitOfWork,
    ITierLimitService tierLimitService,
    IOverageBillingService overageBillingService,
    ILogger<TokenDeductionJob> logger) : ITokenDeductionJob
{
    public async Task DeductTierTokensAsync(string username, int serviceTypeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceType = GetServiceTypeById(serviceTypeId);
            if (serviceType is null)
            {
                logger.LogError("Invalid service type ID {ServiceTypeId} for tier token deduction", serviceTypeId);
                return;
            }

            var user = await unitOfWork.UserRepository.GetByUsernameWithProfileAndTierAsync(username, cancellationToken);
            if (user is null)
            {
                logger.LogError("User {Username} not found for tier token deduction", username);
                return;
            }

            await tierLimitService.AddTokensAsync(user, serviceType, cancellationToken);
            logger.LogDebug("Tier tokens deducted for user {Username}, service {ServiceType}", username, serviceType.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deducting tier tokens for user {Username}, service type {ServiceTypeId}", username, serviceTypeId);
            throw;
        }
    }

    public async Task DeductOverageTokensAsync(string username, int serviceTypeId, decimal tokenCost, CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceType = GetServiceTypeById(serviceTypeId);
            if (serviceType is null)
            {
                logger.LogError("Invalid service type ID {ServiceTypeId} for overage token deduction", serviceTypeId);
                return;
            }

            var user = await unitOfWork.UserRepository.GetByUsernameWithProfileAndTierAsync(username, cancellationToken);
            if (user is null)
            {
                logger.LogError("User {Username} not found for overage token deduction", username);
                return;
            }

            var result = await overageBillingService.DeductTokensWithOverageAsync(user, serviceType, tokenCost, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogError("Failed to deduct overage tokens for user {Username}: {Error}", username, result.Error.Reason);
                return;
            }

            logger.LogDebug(
                "Overage tokens deducted for user {Username}, service {ServiceType}: TokensDeducted={TokensDeducted}, OverageUsed={OverageUsed}, Charge={Charge}",
                username,
                serviceType.Name,
                result.Value.TokensDeducted,
                result.Value.OverageTokensUsed,
                result.Value.OverageCharge);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deducting overage tokens for user {Username}, service type {ServiceTypeId}", username, serviceTypeId);
            throw;
        }
    }

    private static ServiceType? GetServiceTypeById(int id)
    {
        return Enumeration.GetAll<ServiceType>().FirstOrDefault(s => s.Id == id);
    }
}
