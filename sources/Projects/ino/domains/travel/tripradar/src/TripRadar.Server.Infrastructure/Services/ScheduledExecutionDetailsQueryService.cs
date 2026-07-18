using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Constants.ScheduledExecutions;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.ValueObjects;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Services;

public class ScheduledExecutionDetailsQueryService(
    TripRadarDbContext dbContext,
    ILogger<ScheduledExecutionDetailsQueryService> logger,
    IScheduledExecutionRepository scheduledExecutionRepository,
    IRecurringJobService recurringJobService,
    IScheduledExecutionValidityService scheduledExecutionValidityService)
    : IScheduledExecutionDetailsQueryService
{
    public async Task<IReadOnlyList<ScheduledExecutionDetails>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var scheduledExecutions = await dbContext.ScheduledExecutions
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedOn)
            .ToListAsync(cancellationToken);

        if (scheduledExecutions.Count == 0)
        {
            return [];
        }

        var executionIds = scheduledExecutions.Select(e => e.Id).ToArray();

        var flightQueries = await dbContext.ScheduledFlightQueries
            .AsNoTracking()
            .Where(q => executionIds.Contains(q.ScheduledExecutionId))
            .GroupBy(q => q.ScheduledExecutionId)
            .Select(g => g
                .OrderByDescending(q => q.CreatedOn)
                .Select(q => new FlightQuerySummary(
                    q.ScheduledExecutionId,
                    q.CreatedOn,
                    q.DepartureAirport.Code,
                    q.DepartureAirport.City,
                    q.DestinationAirport.Code,
                    q.DestinationAirport.City,
                    q.DepartureDate,
                    q.ReturnDate,
                    q.AdditionalParameters,
                    q.SelectedColumns))
                .First())
            .ToDictionaryAsync(q => q.ScheduledExecutionId, cancellationToken);

        var hotelQueries = await dbContext.ScheduledHotelQueries
            .AsNoTracking()
            .Where(q => executionIds.Contains(q.ScheduledExecutionId))
            .GroupBy(q => q.ScheduledExecutionId)
            .Select(g => g
                .OrderByDescending(q => q.CreatedOn)
                .Select(q => new HotelQuerySummary(
                    q.ScheduledExecutionId,
                    q.CreatedOn,
                    q.Location,
                    q.CheckInDate,
                    q.CheckOutDate,
                    q.AdditionalParameters,
                    q.SelectedColumns))
                .First())
            .ToDictionaryAsync(q => q.ScheduledExecutionId, cancellationToken);

        var eventQueries = await dbContext.ScheduledEventQueries
            .AsNoTracking()
            .Where(q => executionIds.Contains(q.ScheduledExecutionId))
            .GroupBy(q => q.ScheduledExecutionId)
            .Select(g => g
                .OrderByDescending(q => q.CreatedOn)
                .Select(q => new EventQuerySummary(
                    q.ScheduledExecutionId,
                    q.CreatedOn,
                    q.SearchQuery,
                    q.AdditionalParameters,
                    q.SelectedColumns))
                .First())
            .ToDictionaryAsync(q => q.ScheduledExecutionId, cancellationToken);

        var localPlaceQueries = await dbContext.ScheduledLocalPlacesQueries
            .AsNoTracking()
            .Where(q => executionIds.Contains(q.ScheduledExecutionId))
            .GroupBy(q => q.ScheduledExecutionId)
            .Select(g => g
                .OrderByDescending(q => q.CreatedOn)
                .Select(q => new LocalPlaceQuerySummary(
                    q.ScheduledExecutionId,
                    q.CreatedOn,
                    q.SearchQuery,
                    q.AdditionalParameters,
                    q.SelectedColumns))
                .First())
            .ToDictionaryAsync(q => q.ScheduledExecutionId, cancellationToken);

        var details = new List<ScheduledExecutionDetails>(scheduledExecutions.Count);

        foreach (var scheduledExecution in scheduledExecutions)
        {
            try
            {
                ScheduledExecutionDetails detail;
                if (flightQueries.TryGetValue(scheduledExecution.Id, out var flightQuery))
                {
                    detail = CreateFromFlightQuery(scheduledExecution, ScheduledExecutionSearchType.Flights.Name, flightQuery);
                }
                else if (hotelQueries.TryGetValue(scheduledExecution.Id, out var hotelQuery))
                {
                    detail = CreateFromHotelQuery(scheduledExecution, ScheduledExecutionSearchType.Hotels.Name, hotelQuery);
                }
                else if (eventQueries.TryGetValue(scheduledExecution.Id, out var eventQuery))
                {
                    detail = CreateFromEventQuery(scheduledExecution, ScheduledExecutionSearchType.Events.Name, eventQuery, scheduledExecutionValidityService);
                }
                else if (localPlaceQueries.TryGetValue(scheduledExecution.Id, out var localPlaceQuery))
                {
                    detail = CreateFromLocalPlaceQuery(scheduledExecution, ScheduledExecutionSearchType.LocalPlaces.Name, localPlaceQuery);
                }
                else
                {
                    if (RequiresLinkedQuery(scheduledExecution.Name))
                    {
                        await SyncInvalidExecutionAsync(scheduledExecution, cancellationToken);
                        continue;
                    }

                    detail = CreateBaseDetails(scheduledExecution, ResolveServiceTypeName(scheduledExecution.Name), scheduledExecution.Name);
                }

                if (!scheduledExecutionValidityService.IsExecutableAtNextRun(detail))
                {
                    await SyncInvalidExecutionAsync(scheduledExecution, cancellationToken);
                    continue;
                }

                details.Add(detail);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to build scheduled execution details for execution {ScheduledExecutionUniqueId}. Falling back to base details.", scheduledExecution.UniqueId);
                var fallbackDetail = CreateBaseDetails(scheduledExecution, ResolveServiceTypeName(scheduledExecution.Name), scheduledExecution.Name);
                if (!scheduledExecutionValidityService.IsExecutableAtNextRun(fallbackDetail))
                {
                    await SyncInvalidExecutionAsync(scheduledExecution, cancellationToken);
                    continue;
                }

                details.Add(fallbackDetail);
            }
        }

        return details;
    }

    private async Task SyncInvalidExecutionAsync(ScheduledExecution scheduledExecution, CancellationToken cancellationToken)
    {
        try
        {
            if (scheduledExecution.IsActive)
            {
                await scheduledExecutionRepository.UpdateActiveStatusAsync(scheduledExecution.UniqueId, false, cancellationToken);
            }

            recurringJobService.DeleteRecurringExecution(scheduledExecution.UniqueId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to deactivate invalid scheduled execution {ScheduledExecutionUniqueId}.", scheduledExecution.UniqueId);
        }
    }

    private static bool RequiresLinkedQuery(string scheduledExecutionName) =>
        string.Equals(scheduledExecutionName, ScheduledExecutionConstants.ScheduledFlight, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scheduledExecutionName, ScheduledExecutionConstants.ScheduledHotel, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scheduledExecutionName, ScheduledExecutionConstants.ScheduledEvent, StringComparison.OrdinalIgnoreCase);

    private static ScheduledExecutionDetails CreateBaseDetails(ScheduledExecution scheduledExecution, string serviceType, string requestSummary) =>
        new()
        {
            ScheduledExecutionUniqueId = scheduledExecution.UniqueId,
            ServiceType = serviceType,
            IsActive = scheduledExecution.IsActive,
            NextExecutionTime = scheduledExecution.NextExecutionTime,
            Schedule = scheduledExecution.Schedule,
            CreatedOn = scheduledExecution.CreatedOn,
            UpdatedOn = scheduledExecution.UpdatedOn,
            RequestSummary = requestSummary
        };

    private static ScheduledExecutionDetails CreateFromFlightQuery(ScheduledExecution scheduledExecution, string serviceType, FlightQuerySummary flightQuery)
    {
        var departureDisplay = FormatAirportRouteSegment(flightQuery.DepartureAirportCity, flightQuery.DepartureAirportCode);
        var destinationDisplay = FormatAirportRouteSegment(flightQuery.DestinationAirportCity, flightQuery.DestinationAirportCode);
        var summary = departureDisplay is not null && destinationDisplay is not null
            ? $"{departureDisplay} -> {destinationDisplay}"
            : "Flight route";

        var result = CreateBaseDetails(scheduledExecution, serviceType, summary);
        result.DepartureAirportCode = flightQuery.DepartureAirportCode;
        result.DepartureAirportCity = flightQuery.DepartureAirportCity;
        result.DestinationAirportCode = flightQuery.DestinationAirportCode;
        result.DestinationAirportCity = flightQuery.DestinationAirportCity;
        result.DepartureDate = flightQuery.DepartureDate;
        result.ReturnDate = flightQuery.ReturnDate;
        result.AdditionalParameters = flightQuery.AdditionalParameters;
        result.SelectedColumns = flightQuery.SelectedColumns;
        return result;
    }

    private static ScheduledExecutionDetails CreateFromHotelQuery(ScheduledExecution scheduledExecution, string serviceType, HotelQuerySummary hotelQuery)
    {
        var summary = string.IsNullOrWhiteSpace(hotelQuery.Location) ? "Hotel search" : hotelQuery.Location;
        var result = CreateBaseDetails(scheduledExecution, serviceType, summary);
        result.Location = hotelQuery.Location;
        result.CheckInDate = hotelQuery.CheckInDate;
        result.CheckOutDate = hotelQuery.CheckOutDate;
        result.AdditionalParameters = hotelQuery.AdditionalParameters;
        result.SelectedColumns = hotelQuery.SelectedColumns;
        return result;
    }

    private static ScheduledExecutionDetails CreateFromEventQuery(
        ScheduledExecution scheduledExecution,
        string serviceType,
        EventQuerySummary eventQuery,
        IScheduledExecutionValidityService scheduledExecutionValidityService)
    {
        var (location, _) = ExtractLocationAndRadius(eventQuery.AdditionalParameters);
        var summary = string.IsNullOrWhiteSpace(eventQuery.SearchQuery) ? "Event search" : eventQuery.SearchQuery;
        var result = CreateBaseDetails(scheduledExecution, serviceType, summary);
        result.SearchQuery = eventQuery.SearchQuery;
        result.Location = location;
        result.StartDate = scheduledExecutionValidityService.ExtractEventStartDate(eventQuery.AdditionalParameters);
        result.EndDate = scheduledExecutionValidityService.ExtractEventEndDate(eventQuery.AdditionalParameters);
        result.AdditionalParameters = eventQuery.AdditionalParameters;
        result.SelectedColumns = eventQuery.SelectedColumns;
        return result;
    }

    private static ScheduledExecutionDetails CreateFromLocalPlaceQuery(ScheduledExecution scheduledExecution, string serviceType, LocalPlaceQuerySummary localPlaceQuery)
    {
        var (location, radius) = ExtractLocationAndRadius(localPlaceQuery.AdditionalParameters);
        var summary = string.IsNullOrWhiteSpace(location) ? localPlaceQuery.SearchQuery : $"{localPlaceQuery.SearchQuery} ({location})";
        var result = CreateBaseDetails(scheduledExecution, serviceType, summary);
        result.SearchQuery = localPlaceQuery.SearchQuery;
        result.Location = location;
        result.Radius = radius;
        result.AdditionalParameters = localPlaceQuery.AdditionalParameters;
        result.SelectedColumns = localPlaceQuery.SelectedColumns;
        return result;
    }

    private static string? FormatAirportRouteSegment(string? city, string? code)
    {
        if (string.IsNullOrWhiteSpace(city) && string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return code;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return city;
        }

        return $"{city} ({code})";
    }

    private static (string? Location, int? Radius) ExtractLocationAndRadius(string? additionalParameters)
    {
        if (string.IsNullOrWhiteSpace(additionalParameters))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(additionalParameters);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var location = root.TryGetProperty("location", out var locationElement)
                ? locationElement.ValueKind switch
                {
                    JsonValueKind.String => locationElement.GetString(),
                    JsonValueKind.Number => locationElement.GetRawText(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => null
                }
                : null;

            int? radius = null;
            if (root.TryGetProperty("radius", out var radiusElement))
            {
                radius = radiusElement.ValueKind switch
                {
                    JsonValueKind.Number when radiusElement.TryGetInt32(out var numberRadius) => numberRadius,
                    JsonValueKind.String when int.TryParse(radiusElement.GetString(), out var parsedRadius) => parsedRadius,
                    _ => radius
                };
            }

            return (location, radius);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    private static string ResolveServiceTypeName(string scheduledExecutionName)
    {
        if (scheduledExecutionName.Contains(ScheduledExecutionConstants.ScheduledFlight, StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Flights.Name;
        }

        if (scheduledExecutionName.Contains(ScheduledExecutionConstants.ScheduledHotel, StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Hotels.Name;
        }

        if (scheduledExecutionName.Contains(ScheduledExecutionConstants.ScheduledEvent, StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Events.Name;
        }

        if (scheduledExecutionName.Contains(ScheduledExecutionConstants.ScheduledLocalPlaces, StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.LocalPlaces.Name;
        }

        return scheduledExecutionName;
    }

    private sealed record FlightQuerySummary(
        long ScheduledExecutionId,
        DateTime CreatedOn,
        string? DepartureAirportCode,
        string? DepartureAirportCity,
        string? DestinationAirportCode,
        string? DestinationAirportCity,
        DateTime DepartureDate,
        DateTime? ReturnDate,
        string? AdditionalParameters,
        IList<QueryColumn>? SelectedColumns);

    private sealed record HotelQuerySummary(
        long ScheduledExecutionId,
        DateTime CreatedOn,
        string Location,
        DateTime CheckInDate,
        DateTime CheckOutDate,
        string? AdditionalParameters,
        IList<QueryColumn>? SelectedColumns);

    private sealed record EventQuerySummary(
        long ScheduledExecutionId,
        DateTime CreatedOn,
        string SearchQuery,
        string? AdditionalParameters,
        IList<QueryColumn>? SelectedColumns);

    private sealed record LocalPlaceQuerySummary(
        long ScheduledExecutionId,
        DateTime CreatedOn,
        string SearchQuery,
        string? AdditionalParameters,
        IList<QueryColumn>? SelectedColumns);
}




