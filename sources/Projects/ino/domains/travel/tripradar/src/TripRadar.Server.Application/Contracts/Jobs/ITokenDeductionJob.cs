namespace TripRadar.Server.Application.Contracts.Jobs;

public interface ITokenDeductionJob
{
    Task DeductTierTokensAsync(string username, int serviceTypeId, CancellationToken cancellationToken = default);

    Task DeductOverageTokensAsync(string username, int serviceTypeId, decimal tokenCost, CancellationToken cancellationToken = default);
}
