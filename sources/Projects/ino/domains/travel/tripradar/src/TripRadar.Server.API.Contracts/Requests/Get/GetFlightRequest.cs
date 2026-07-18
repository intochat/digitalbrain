using System.ComponentModel.DataAnnotations;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class GetFlightRequest : IValidatableObject
{

    public string? TripVaultName { get; set; }

    public FlightSearchQuery? FlightSearch { get; set; }

    public Localization? Localization { get; set; }

    [Required] public required AdvancedSearchOptions AdvancedOptions { get; set; }

    public PassengerInfo? Passengers { get; set; }

    public SortingOptions? Sorting { get; set; }

    public AdvancedFilters? Filters { get; set; }

    public NextFlights? NextFlights { get; set; }

    public BookingFlights? Booking { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasBookingToken = !string.IsNullOrWhiteSpace(Booking?.BookingToken);

        // For OneWay and RoundTrip, flightSearch and outboundDate are required
        if (!hasBookingToken && AdvancedOptions.Type != Enums.FlightType.MultiCity)
        {
            if (FlightSearch == null)
            {
                yield return new ValidationResult("FlightSearch is required for OneWay and RoundTrip flights.", [nameof(FlightSearch)]);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(FlightSearch.DepartureId))
                {
                    yield return new ValidationResult("Departure airport code is required for OneWay and RoundTrip flights.", [nameof(FlightSearch.DepartureId)]);
                }

                if (string.IsNullOrWhiteSpace(FlightSearch.ArrivalId))
                {
                    yield return new ValidationResult("Arrival airport code is required for OneWay and RoundTrip flights.", [nameof(FlightSearch.ArrivalId)]);
                }
            }

            if (string.IsNullOrWhiteSpace(AdvancedOptions.OutboundDate))
            {
                yield return new ValidationResult("Outbound date is required for OneWay and RoundTrip flights.", [nameof(AdvancedOptions.OutboundDate)]);
            }
        }

        // For MultiCity, multiCityJson is required
        if (!hasBookingToken && AdvancedOptions.Type == Enums.FlightType.MultiCity)
        {
            if (AdvancedOptions.MultiCityJson == null || AdvancedOptions.MultiCityJson.Count < 2)
            {
                yield return new ValidationResult("MultiCity flights require at least 2 legs in multiCityJson.", [nameof(AdvancedOptions.MultiCityJson)]);
            }
        }

        if (!string.IsNullOrWhiteSpace(AdvancedOptions.ReturnDate))
        {
            if (DateTime.TryParse(AdvancedOptions.OutboundDate, out var outboundDate) && DateTime.TryParse(AdvancedOptions.ReturnDate, out var returnDate))
            {
                if (returnDate < outboundDate)
                {
                    yield return new ValidationResult("Return date must be after departure date.", [nameof(AdvancedOptions.OutboundDate), nameof(AdvancedOptions.ReturnDate)]);
                }
            }
        }

        if (Passengers != null)
        {
            var totalPassengers = (Passengers.Adults ?? 0) + (Passengers.Children ?? 0) + (Passengers.InfantsInSeat ?? 0) + (Passengers.InfantsOnLap ?? 0);
            if (totalPassengers > 9)
            {
                yield return new ValidationResult("Maximum 9 passengers allowed per search.", [nameof(Passengers)]);
            }
        }

        if (NextFlights != null && Booking != null && !string.IsNullOrWhiteSpace(NextFlights.DepartureToken) && !string.IsNullOrWhiteSpace(Booking.BookingToken))
        {
            yield return new ValidationResult("departure_token and booking_token cannot both be set.", [nameof(NextFlights.DepartureToken), nameof(Booking.BookingToken)]);
        }
    }
}

