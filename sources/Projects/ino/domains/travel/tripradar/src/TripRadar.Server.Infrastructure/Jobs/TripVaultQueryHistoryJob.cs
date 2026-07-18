using TripRadar.Server.Application.Contracts.Jobs;
using TripRadar.Server.Application.Contracts.Services;

namespace TripRadar.Server.Infrastructure.Jobs;

public class TripVaultQueryHistoryJob(ITripVaultQuerySaver tripVaultQuerySaver) : ITripVaultQueryHistoryJob
{
    public Task SaveAsync(
        Guid tripVaultUniqueId,
        int serviceTypeId,
        string queryParametersJson,
        string? resultSummary = null,
        CancellationToken cancellationToken = default) =>
        tripVaultQuerySaver.TrySaveSerializedQueryAsync(
            tripVaultUniqueId,
            serviceTypeId,
            queryParametersJson,
            resultSummary,
            cancellationToken);
}
