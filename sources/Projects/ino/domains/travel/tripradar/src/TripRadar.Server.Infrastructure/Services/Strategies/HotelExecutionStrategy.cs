using AutoMapper;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Contracts.Scheduled;
using TripRadar.Server.Infrastructure.Extensions;
using TripRadar.Server.Infrastructure.Services.Handlers;
using ServiceType = TripRadar.Server.Domain.Enums.ServiceType;

namespace TripRadar.Server.Infrastructure.Services.Strategies;

public class HotelExecutionStrategy(
    IUnitOfWork unitOfWork,
    IMapper mapper,
    IPreferenceService preferenceService,
    HotelSerpApiHandler hotelHandler,
    ITripVaultQuerySaver tripVaultQuerySaver,
    ILogger<HotelExecutionStrategy> logger) : IScheduledExecutionStrategy
{
    public bool CanHandle(ScheduledExecutionSearchType searchType) => Equals(searchType, ScheduledExecutionSearchType.Hotels);

    public async Task<bool> ExecuteAsync(ScheduledExecution scheduledExecution, CancellationToken cancellationToken = default)
    {
        var request = await scheduledExecution.ResolveGoogleServicesScheduledQueryAsync(unitOfWork, mapper, cancellationToken);
        if (request is null)
        {
            logger.LogInformation("Scheduled hotel execution with unique id - {ExecutionId} is not valid, skipping execution", scheduledExecution.UniqueId);
            return false;
        }

        var applied = await preferenceService.AddPreferencesAsync((GetHotelRequestDTO)request, scheduledExecution.UserId, ServiceType.Hotel, cancellationToken);

        if (applied.IsFailure)
        {
            logger.LogError("Failed to apply preferences for scheduled hotel execution {ExecutionId}: {Error}", scheduledExecution.UniqueId, applied.Error);
            return false;
        }

        await hotelHandler.HandleSearchAsync(scheduledExecution, applied.Value!, cancellationToken);

        await SaveQueryHistoryAsync(scheduledExecution, ServiceType.Hotel, applied.Value!, cancellationToken);

        return true;
    }

    private async Task SaveQueryHistoryAsync<TRequest>(
        ScheduledExecution scheduledExecution,
        ServiceType serviceType,
        TRequest request,
        CancellationToken cancellationToken)
    {
        if (!scheduledExecution.TripVaultId.HasValue)
            return;

        var tripVault = await unitOfWork.TripVaultRepository.GetByIdAsync(scheduledExecution.TripVaultId.Value, cancellationToken);
        if (tripVault is null)
            return;

        await tripVaultQuerySaver.TrySaveQueryAsync(tripVault.UniqueId, serviceType, request, cancellationToken: cancellationToken);
    }
}
