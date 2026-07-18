using System.Text.RegularExpressions;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlights;

public partial class GetFlightsQueryCustomValidator(
    IAirportValidationService airportValidationService,
    IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetFlightsQuery>
{
    public async Task<Error?> ValidateAsync(GetFlightsQuery request, CancellationToken cancellationToken)
    {
        if (request.GetFlightRequestDto.AdvancedOptions.Type == FlightType.MultiCity)
        {
            return null;
        }

        if (request.GetFlightRequestDto.Booking?.BookingToken is not null)
        {
            return null;
        }

        if (request.GetFlightRequestDto.FlightSearch is null)
        {
            return Errors.AirportCodeNotFound with { Reason = "Flight search parameters are required." };
        }

        var departureInput = request.GetFlightRequestDto.FlightSearch.DepartureId;
        var arrivalInput = request.GetFlightRequestDto.FlightSearch.ArrivalId;

        if (!IsMultiAirportCodes(departureInput))
        {
            var resolvedDeparture = await airportValidationService.ResolveAirportCodeAsync(departureInput, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolvedDeparture))
                request.GetFlightRequestDto.FlightSearch.DepartureId = resolvedDeparture;
        }

        if (!IsMultiAirportCodes(arrivalInput))
        {
            var resolvedArrival = await airportValidationService.ResolveAirportCodeAsync(arrivalInput, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolvedArrival))
                request.GetFlightRequestDto.FlightSearch.ArrivalId = resolvedArrival;
        }

        var departureCode = request.GetFlightRequestDto.FlightSearch.DepartureId;
        if (string.IsNullOrWhiteSpace(departureCode))
        {
            return Errors.AirportCodeNotFound with { Reason = "Departure airport code was not found." };
        }

        var arrivalCode = request.GetFlightRequestDto.FlightSearch.ArrivalId;
        if (string.IsNullOrWhiteSpace(arrivalCode))
        {
            return Errors.AirportCodeNotFound with { Reason = "Arrival airport code was not found." };
        }

        var departureCodes = departureCode.Split(',');
        var arrivalCodes = arrivalCode.Split(',');

        foreach (var code in departureCodes.Concat(arrivalCodes))
        {
            var resolved = await airportValidationService.ResolveAirportCodeAsync(code, cancellationToken);
            if (string.IsNullOrWhiteSpace(resolved))
                return Errors.AirportCodeNotFound with { Reason = $"Airport code '{code}' was not found." };
        }

        if (departureCodes.Intersect(arrivalCodes).Any())
            return Errors.InvalidFlightRoute;

        var includeAirlinesError = await referenceLookupValidator.ValidateAirlineCodesAsync(request.GetFlightRequestDto.Filters?.IncludeAirlines, cancellationToken);

        if (includeAirlinesError is not null)
        {
            return includeAirlinesError;
        }

        return await referenceLookupValidator.ValidateAirlineCodesAsync(request.GetFlightRequestDto.Filters?.ExcludeAirlines, cancellationToken);
    }

    private static bool IsMultiAirportCodes(string? input) =>
        input is not null && input.Contains(',') && MultiAirportPattern().IsMatch(input.Trim());

    [GeneratedRegex(@"^[A-Z]{3}(,[A-Z]{3})+$")]
    private static partial Regex MultiAirportPattern();
}
