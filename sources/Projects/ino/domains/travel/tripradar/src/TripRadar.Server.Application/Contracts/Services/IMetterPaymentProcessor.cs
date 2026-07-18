namespace TripRadar.Server.Application.Contracts.Services;

public interface IMetterPaymentProcessor
{
    Task<int> ClearStaleProcessingAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);

    Task<int> ProcessMonthlyOverageChargesAsync(CancellationToken cancellationToken = default);
}
