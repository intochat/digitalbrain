using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.Contracts.Messaging;
using TripRadar.Server.Comms.Core.Events;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.ValueObjects;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Contracts.Handlers;
using TripRadar.Server.Infrastructure.Models;

namespace TripRadar.Server.Infrastructure.Services.Handlers;

public abstract class SerpApiHandler<TRequest, TResponse, TEntity, TEvent>(
    ISerpApiProviderService serpApiProviderService,
    IProducerService producerService,
    ISearchResponseFilter<TResponse> responseFilter,
    ILogger logger) : ISearchTypeHandler where TRequest : class, ISerpApiRequest
    where TResponse : class
    where TEntity : class
    where TEvent : PublishableEvent, new()
{
    public async Task HandleSearchAsync(ScheduledExecution scheduledExecution, ISerpApiRequest request, CancellationToken cancellation)
    {
        var data = await serpApiProviderService.SearchAsync<TRequest, TResponse>((TRequest)request, cancellation);

        if (data.IsSuccess)
        {
            var scheduledQuery = await GetScheduledQueryAsync(scheduledExecution.Id, cancellation);
            if (data.Value != null)
            {
                var filteredResponse = responseFilter.Filter(data.Value, GetSelectedColumns(scheduledQuery));
                await SendEventAsync(filteredResponse, scheduledExecution, cancellation);
            }
        }
        else if (data.IsFailure)
        {
            logger.LogError(
                "Scheduled query failed with error code {ErrorCode} and reason {ErrorReason}",
                data.Error.Code,
                data.Error.Reason);
        }
    }

    protected abstract Task<TEntity?> GetScheduledQueryAsync(long executionId, CancellationToken cancellation);
    protected abstract IList<QueryColumn>? GetSelectedColumns(TEntity? scheduledQuery);

    private async Task SendEventAsync(object data, ScheduledExecution scheduledExecution, CancellationToken cancellation) =>
        await producerService.ProduceAsync(new TEvent { EventId = Guid.NewGuid(), EventOwner = new Owner { Username = scheduledExecution.User.Profile.Username ?? string.Empty }, EventData = data, EventDate = DateTime.UtcNow }, cancellation);
}

public class EventSerpApiHandler(
    IUnitOfWork unitOfWork,
    ISerpApiProviderService serpApiProviderService,
    IProducerService producerService,
    ISearchResponseFilter<GetEventResponseDTO> eventResponseFilter,
    ILogger<EventSerpApiHandler> logger)
    : SerpApiHandler<GetEventRequestDTO, GetEventResponseDTO, ScheduledEventQuery, Events.EventScheduledQuery>(
        serpApiProviderService, producerService, eventResponseFilter, logger)
{
    protected override async Task<ScheduledEventQuery?> GetScheduledQueryAsync(long executionId, CancellationToken cancellation) => await unitOfWork.ScheduledEventQueryRepository.GetByScheduledExecutionIdAsync(executionId, cancellation);

    protected override IList<QueryColumn>? GetSelectedColumns(ScheduledEventQuery? scheduledQuery) => scheduledQuery?.SelectedColumns;
}

public class FlightSerpApiHandler(
    IUnitOfWork unitOfWork,
    ISerpApiProviderService serpApiProviderService,
    IProducerService producerService,
    ISearchResponseFilter<GetFlightResponseDTO> flightResponseFilter,
    ILogger<FlightSerpApiHandler> logger)
    : SerpApiHandler<GetFlightRequestDTO, GetFlightResponseDTO, ScheduledFlightQuery,
        Events.FlightScheduledQuery>(serpApiProviderService, producerService, flightResponseFilter, logger)
{
    protected override async Task<ScheduledFlightQuery?> GetScheduledQueryAsync(long executionId, CancellationToken cancellation) => await unitOfWork.ScheduledFlightQueryRepository.GetByScheduledExecutionIdAsync(executionId, cancellation);

    protected override IList<QueryColumn>? GetSelectedColumns(ScheduledFlightQuery? scheduledQuery) => scheduledQuery?.SelectedColumns;
}

public class LocalPlacesSerpApiHandler(
    IUnitOfWork unitOfWork,
    ISerpApiProviderService serpApiProviderService,
    IProducerService producerService,
    ISearchResponseFilter<GetLocalPlacesResponseDTO> localPlacesResponseFilter,
    ILogger<LocalPlacesSerpApiHandler> logger)
    : SerpApiHandler<GetLocalPlacesRequestDTO, GetLocalPlacesResponseDTO, ScheduledLocalPlaceQuery,
        Events.LocalPlacesScheduledQuery>(serpApiProviderService, producerService, localPlacesResponseFilter, logger)
{
    protected override async Task<ScheduledLocalPlaceQuery?> GetScheduledQueryAsync(long executionId, CancellationToken cancellation) => await unitOfWork.ScheduledLocalPlacesQueryRepository.GetByScheduledExecutionIdAsync(executionId, cancellation);

    protected override IList<QueryColumn>? GetSelectedColumns(ScheduledLocalPlaceQuery? scheduledQuery) => scheduledQuery?.SelectedColumns;
}

public class HotelSerpApiHandler(
    IUnitOfWork unitOfWork,
    ISerpApiProviderService serpApiProviderService,
    IProducerService producerService,
    ISearchResponseFilter<GetHotelResponseDTO> hotelResponseFilter,
    ILogger<HotelSerpApiHandler> logger)
    : SerpApiHandler<GetHotelRequestDTO, GetHotelResponseDTO, ScheduledHotelQuery, Events.HotelScheduledQuery>(
        serpApiProviderService, producerService, hotelResponseFilter, logger)
{
    protected override async Task<ScheduledHotelQuery?> GetScheduledQueryAsync(long executionId, CancellationToken cancellation) => await unitOfWork.ScheduledHotelQueryRepository.GetByScheduledExecutionIdAsync(executionId, cancellation);

    protected override IList<QueryColumn>? GetSelectedColumns(ScheduledHotelQuery? scheduledQuery) => scheduledQuery?.SelectedColumns;
}
