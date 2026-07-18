using AutoMapper;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.Extensions;
using TripRadar.Server.Comms.Core.Exceptions;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Extensions;

public static class ScheduledExecutionServiceExtensions
{
    public static async Task<ISerpApiRequest?> ResolveGoogleServicesScheduledQueryAsync(
        this ScheduledExecution scheduledExecution, IUnitOfWork unitOfWork, IMapper mapper,
        CancellationToken cancellationToken = default)
    {
        var searchType = scheduledExecution.GetSearchType();

        if (Equals(searchType, ScheduledExecutionSearchType.Flights))
            return await ResolveQueryAsync(unitOfWork.ScheduledFlightQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken), mapper.Map<GetFlightRequestDTO>, scheduledExecution.Id);

        if (Equals(searchType, ScheduledExecutionSearchType.Hotels))
            return await ResolveQueryAsync(unitOfWork.ScheduledHotelQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken), mapper.Map<GetHotelRequestDTO>, scheduledExecution.Id);

        if (Equals(searchType, ScheduledExecutionSearchType.Events))
            return await ResolveQueryAsync(unitOfWork.ScheduledEventQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken), mapper.Map<GetEventRequestDTO>, scheduledExecution.Id);

        if (Equals(searchType, ScheduledExecutionSearchType.LocalPlaces))
            return await ResolveQueryAsync(unitOfWork.ScheduledLocalPlacesQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken), mapper.Map<GetLocalPlacesRequestDTO>, scheduledExecution.Id);

        return null;
    }

    private static async Task<ISerpApiRequest?> ResolveQueryAsync<TEntity>(
        Task<TEntity?> queryTask,
        Func<TEntity, ISerpApiRequest> mapper,
        long executionId) where TEntity : class
    {
        var entity = await queryTask;
        return entity is null ? throw new InvalidRequestException($"Scheduled query with execution id {executionId} is null!") : mapper(entity);
    }

}
