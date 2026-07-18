using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services;

public class TripVaultQuerySaver(IUnitOfWork unitOfWork, ILogger<TripVaultQuerySaver> logger) : ITripVaultQuerySaver
{
    private static readonly JsonSerializerOptions QueryPayloadJsonSerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Task TrySaveQueryAsync<TRequest>(Guid? tripVaultUniqueId, ServiceType serviceType, TRequest request, string? resultSummary = null, CancellationToken cancellationToken = default)
    {
        if (RequestPrivacyMode.IsAnonymous(request))
        {
            return Task.CompletedTask;
        }

        var queryParametersJson = JsonSerializer.Serialize(request, QueryPayloadJsonSerializerOptions);
        return TrySaveSerializedQueryAsync(tripVaultUniqueId, serviceType.Id, queryParametersJson, resultSummary, cancellationToken);
    }

    public async Task TrySaveSerializedQueryAsync(
        Guid? tripVaultUniqueId,
        int serviceTypeId,
        string queryParametersJson,
        string? resultSummary = null,
        CancellationToken cancellationToken = default)
    {
        if (!tripVaultUniqueId.HasValue || string.IsNullOrWhiteSpace(queryParametersJson))
        {
            return;
        }

        try
        {
            var tripVault = await unitOfWork.TripVaultRepository.GetByUniqueIdForUpdateAsync(tripVaultUniqueId.Value, cancellationToken);
            if (tripVault is null)
            {
                return;
            }

            tripVault.AddItem(serviceTypeId, queryParametersJson, resultSummary: resultSummary);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to save query history to TripVault {TripVaultUniqueId} for service {ServiceTypeId}. Error: {ErrorMessage}",
                tripVaultUniqueId.Value,
                serviceTypeId,
                ex.Message);
        }
    }
}
