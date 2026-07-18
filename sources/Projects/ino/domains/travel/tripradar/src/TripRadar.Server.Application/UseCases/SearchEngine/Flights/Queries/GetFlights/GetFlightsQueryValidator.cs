using FluentValidation;
using TripRadar.Server.Application.DTO.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlights;

public class GetFlightsQueryValidator : AbstractValidator<GetFlightsQuery>
{
    public GetFlightsQueryValidator()
    {
        When(command => command.GetFlightRequestDto.AdvancedOptions.Type != FlightType.MultiCity
                       && command.GetFlightRequestDto.Booking?.BookingToken is null
                       && command.GetFlightRequestDto.FlightSearch is not null, () =>
        {
            RuleFor(command => command.GetFlightRequestDto.FlightSearch!.DepartureId)
                .NotEmpty()
                .WithMessage("'DepartureId' is a required field for OneWay and RoundTrip flights.")
                .MaximumLength(60).WithMessage("'DepartureId' must not exceed 60 characters.");

            RuleFor(command => command.GetFlightRequestDto.FlightSearch!.ArrivalId)
                .NotEmpty()
                .WithMessage("'ArrivalId' is a required field for OneWay and RoundTrip flights.")
                .MaximumLength(60)
                .WithMessage("'ArrivalId' must not exceed 60 characters.")
                .Must((command, arrivalId) => BeDifferentFrom(arrivalId, command.GetFlightRequestDto.FlightSearch!.DepartureId))
                .WithMessage("'ArrivalId' must be different from 'DepartureId'.");

            RuleFor(command => command.GetFlightRequestDto.AdvancedOptions.OutboundDate)
                .NotEmpty()
                .WithMessage("'OutboundDate' is a required field for OneWay and RoundTrip flights.");
        });

        When(command => command.GetFlightRequestDto.AdvancedOptions.Type == FlightType.MultiCity, () =>
        {
            RuleFor(command => command.GetFlightRequestDto.AdvancedOptions.MultiCityJson)
                .NotNull()
                .WithMessage("'MultiCityJson' is required for MultiCity flights.")
                .Must(legs => legs != null && legs.Count >= 2)
                .WithMessage("MultiCity flights require at least 2 legs in 'MultiCityJson'.");
        });

        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("'Username' is a required field.")
            .MaximumLength(60).WithMessage("'Username' must not exceed 60 characters.");

        RuleFor(command => command)
            .Must(ValidateDateRange)
            .WithMessage("Return date must be after departure date for round-trip flights.");

        RuleFor(command => command)
            .Must(ValidatePassengerCount)
            .WithMessage("Total passenger count (adults + children + infants) cannot exceed 9.");
    }

    private static bool BeDifferentFrom(string? source, string? destination)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            return true;
        return !string.Equals(source, destination, StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool ValidateDateRange(GetFlightsQuery query)
    {
        if (query.GetFlightRequestDto.AdvancedOptions.Type == FlightType.RoundTrip)
        {
            if (string.IsNullOrEmpty(query.GetFlightRequestDto.AdvancedOptions.ReturnDate))
                return true;

            if (DateOnly.TryParse(query.GetFlightRequestDto.AdvancedOptions.OutboundDate, out var departureDate) &&
                DateOnly.TryParse(query.GetFlightRequestDto.AdvancedOptions.ReturnDate, out var returnDate))
            {
                return returnDate > departureDate;
            }
        }
        return true;
    }

    private static bool ValidatePassengerCount(GetFlightsQuery query)
    {
        var passengers = query.GetFlightRequestDto.Passengers;
        if (passengers == null)
            return true;

        var totalPassengers = (passengers.Adults ?? 0) + (passengers.Children ?? 0) +
                             (passengers.InfantsInSeat ?? 0) + (passengers.InfantsOnLap ?? 0);
        return totalPassengers <= 9;
    }
}
