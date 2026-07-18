using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface ITripVaultQuerySaver
{
    Task TrySaveQueryAsync<TRequest>(
        Guid? tripVaultUniqueId,
        ServiceType serviceType,
        TRequest request,
        string? resultSummary = null,
        CancellationToken cancellationToken = default);

    Task TrySaveSerializedQueryAsync(
        Guid? tripVaultUniqueId,
        int serviceTypeId,
        string queryParametersJson,
        string? resultSummary = null,
        CancellationToken cancellationToken = default);
}
