using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Constants.ScheduledExecutions;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionQuery;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Services;

public sealed class ScheduledExecutionQueryUpdater(
    IScheduledFlightQueryRepository scheduledFlightQueryRepository,
    IScheduledHotelQueryRepository scheduledHotelQueryRepository,
    IScheduledEventQueryRepository scheduledEventQueryRepository,
    IScheduledLocalPlacesQueryRepository scheduledLocalPlacesQueryRepository,
    IAirportRepository airportRepository,
    IScheduledExecutionValidityService scheduledExecutionValidityService)
    : IScheduledExecutionQueryUpdater
{
    public async Task<Result> UpdateAsync(
        ScheduledExecution scheduledExecution,
        UpdateScheduledExecutionQueryCommand request,
        CancellationToken cancellationToken)
    {
        var searchType = ResolveSearchType(scheduledExecution.Name);
        if (searchType is null)
        {
            return Result.Failure(Errors.SearchTypeNotFound);
        }

        if (Equals(searchType, ScheduledExecutionSearchType.Flights))
        {
            return await UpdateFlightQueryAsync(scheduledExecution, request, cancellationToken);
        }

        if (Equals(searchType, ScheduledExecutionSearchType.Hotels))
        {
            return await UpdateHotelQueryAsync(scheduledExecution, request, cancellationToken);
        }

        if (Equals(searchType, ScheduledExecutionSearchType.Events))
        {
            return await UpdateEventQueryAsync(scheduledExecution, request, cancellationToken);
        }

        if (Equals(searchType, ScheduledExecutionSearchType.LocalPlaces))
        {
            return await UpdateLocalPlacesQueryAsync(scheduledExecution.Id, request, cancellationToken);
        }

        return Result.Failure(Errors.SearchTypeNotFound);
    }

    private async Task<Result> UpdateFlightQueryAsync(
        ScheduledExecution scheduledExecution,
        UpdateScheduledExecutionQueryCommand request,
        CancellationToken cancellationToken)
    {
        var scheduledFlightQuery = await scheduledFlightQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken);
        if (scheduledFlightQuery is null)
        {
            return Result.Failure(Errors.FlightQueryDataNotFound);
        }

        var departureAirportId = scheduledFlightQuery.DepartureAirportId;
        var destinationAirportId = scheduledFlightQuery.DestinationAirportId;

        if (!string.IsNullOrWhiteSpace(request.DepartureAirportCode))
        {
            var departureAirport = await ResolveAirportAsync(request.DepartureAirportCode, cancellationToken);
            if (departureAirport is null)
            {
                return Result.Failure(Errors.AirportCodeNotFound);
            }

            departureAirportId = departureAirport.Id;
        }

        if (!string.IsNullOrWhiteSpace(request.DestinationAirportCode))
        {
            var destinationAirport = await ResolveAirportAsync(request.DestinationAirportCode, cancellationToken);
            if (destinationAirport is null)
            {
                return Result.Failure(Errors.AirportCodeNotFound);
            }

            destinationAirportId = destinationAirport.Id;
        }

        if (departureAirportId == destinationAirportId)
        {
            return Result.Failure(Errors.InvalidFlightRoute);
        }

        var departureDate = request.DepartureDate ?? scheduledFlightQuery.DepartureDate;
        var returnDate = request.ReturnDate ?? scheduledFlightQuery.ReturnDate;
        if (returnDate.HasValue && returnDate.Value <= departureDate)
        {
            return Result.Failure(Errors.InvalidFlightDates);
        }

        if (!scheduledExecutionValidityService.IsExecutableAtNextRun(ScheduledExecutionSearchType.Flights, scheduledExecution.NextExecutionTime, departureDate))
        {
            return Result.Failure(Errors.InvalidScheduledExecutionWindow);
        }

        var additionalParameters = scheduledFlightQuery.AdditionalParameters.MergeJsonObjects(request.AdditionalParametersJson);
        var selectedColumns = request.SelectedColumns ?? scheduledFlightQuery.SelectedColumns;

        scheduledFlightQuery.Update(departureAirportId, destinationAirportId, departureDate, returnDate, additionalParameters, selectedColumns);
        return Result.Success();
    }

    private async Task<Result> UpdateHotelQueryAsync(
        ScheduledExecution scheduledExecution,
        UpdateScheduledExecutionQueryCommand request,
        CancellationToken cancellationToken)
    {
        var scheduledHotelQuery = await scheduledHotelQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken);
        if (scheduledHotelQuery is null)
        {
            return Result.Failure(Errors.HotelQueryDataNotFound);
        }

        var location = string.IsNullOrWhiteSpace(request.Location)
            ? scheduledHotelQuery.Location
            : request.Location.Trim();

        var checkInDate = request.CheckInDate ?? scheduledHotelQuery.CheckInDate;
        var checkOutDate = request.CheckOutDate ?? scheduledHotelQuery.CheckOutDate;
        if (checkOutDate <= checkInDate)
        {
            return Result.Failure(Errors.InvalidHotelDates);
        }

        if (!scheduledExecutionValidityService.IsExecutableAtNextRun(ScheduledExecutionSearchType.Hotels, scheduledExecution.NextExecutionTime, checkInDate))
        {
            return Result.Failure(Errors.InvalidScheduledExecutionWindow);
        }

        var additionalParameters = scheduledHotelQuery.AdditionalParameters.MergeJsonObjects(request.AdditionalParametersJson);
        var selectedColumns = request.SelectedColumns ?? scheduledHotelQuery.SelectedColumns;

        scheduledHotelQuery.Update(location, checkInDate, checkOutDate, additionalParameters, selectedColumns);
        return Result.Success();
    }

    private async Task<Result> UpdateEventQueryAsync(
        ScheduledExecution scheduledExecution,
        UpdateScheduledExecutionQueryCommand request,
        CancellationToken cancellationToken)
    {
        var scheduledEventQuery = await scheduledEventQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecution.Id, cancellationToken);
        if (scheduledEventQuery is null)
        {
            return Result.Failure(Errors.EventQueryDataNotFound);
        }

        var searchQuery = ResolveQueryValue(request.SearchQuery, scheduledEventQuery.SearchQuery);
        var overrides = CreateOverrides(("location", request.Location, !string.IsNullOrWhiteSpace(request.Location)));
        var additionalParameters = scheduledEventQuery.AdditionalParameters.MergeJsonObjects(request.AdditionalParametersJson, overrides);
        var selectedColumns = request.SelectedColumns ?? scheduledEventQuery.SelectedColumns;
        var startDate = scheduledExecutionValidityService.ExtractEventStartDate(additionalParameters);

        if (!scheduledExecutionValidityService.IsExecutableAtNextRun(ScheduledExecutionSearchType.Events, scheduledExecution.NextExecutionTime, startDate))
        {
            return Result.Failure(Errors.InvalidScheduledExecutionWindow);
        }

        scheduledEventQuery.Update(searchQuery, additionalParameters, selectedColumns);
        return Result.Success();
    }

    private async Task<Result> UpdateLocalPlacesQueryAsync(
        long scheduledExecutionId,
        UpdateScheduledExecutionQueryCommand request,
        CancellationToken cancellationToken)
    {
        var scheduledLocalPlacesQuery = await scheduledLocalPlacesQueryRepository.GetByScheduledExecutionIdAsync(scheduledExecutionId, cancellationToken);
        if (scheduledLocalPlacesQuery is null)
        {
            return Result.Failure(Errors.LocalPlacesQueryDataNotFound);
        }

        var searchQuery = ResolveQueryValue(request.SearchQuery, scheduledLocalPlacesQuery.SearchQuery);
        var overrides = CreateOverrides(
            ("location", request.Location, !string.IsNullOrWhiteSpace(request.Location)),
            ("radius", request.Radius, request.Radius.HasValue));
        var additionalParameters = scheduledLocalPlacesQuery.AdditionalParameters.MergeJsonObjects(request.AdditionalParametersJson, overrides);
        var selectedColumns = request.SelectedColumns ?? scheduledLocalPlacesQuery.SelectedColumns;

        scheduledLocalPlacesQuery.Update(searchQuery, additionalParameters, selectedColumns);
        return Result.Success();
    }

    private async Task<Airport?> ResolveAirportAsync(string code, CancellationToken cancellationToken)
    {
        return await airportRepository.GetByCodeAsync(code.Trim().ToUpperInvariant(), cancellationToken);
    }

    private static string ResolveQueryValue(string? updatedValue, string existingValue)
    {
        return string.IsNullOrWhiteSpace(updatedValue) ? existingValue : updatedValue.Trim();
    }

    private static ScheduledExecutionSearchType? ResolveSearchType(string scheduledExecutionName)
    {
        if (scheduledExecutionName.Contains(ScheduledExecutionConstants.ScheduledFlight, StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Flights;
        }

        if (scheduledExecutionName.Contains(ScheduledExecutionConstants.ScheduledHotel, StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Hotels;
        }

        if (scheduledExecutionName.Contains(ScheduledExecutionConstants.ScheduledEvent, StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.Events;
        }

        if (scheduledExecutionName.Contains(ScheduledExecutionConstants.ScheduledLocalPlaces, StringComparison.OrdinalIgnoreCase))
        {
            return ScheduledExecutionSearchType.LocalPlaces;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, object?>? CreateOverrides(params (string Key, object? Value, bool Apply)[] candidates)
    {
        var overrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value, apply) in candidates)
        {
            if (!apply)
            {
                continue;
            }

            overrides[key] = value;
        }

        return overrides.Count == 0 ? null : overrides;
    }
}
