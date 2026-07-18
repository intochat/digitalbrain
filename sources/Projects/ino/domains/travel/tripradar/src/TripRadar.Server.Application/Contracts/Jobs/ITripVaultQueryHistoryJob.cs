namespace TripRadar.Server.Application.Contracts.Jobs;

public interface ITripVaultQueryHistoryJob
{
    Task SaveAsync(
        Guid tripVaultUniqueId,
        int serviceTypeId,
        string queryParametersJson,
        string? resultSummary = null,
        CancellationToken cancellationToken = default);
}
