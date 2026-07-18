using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightExplore;

public class GetFlightExploreQueryValidator : AbstractValidator<GetFlightExploreQuery>
{
    public GetFlightExploreQueryValidator()
    {
        RuleFor(q => q.Username)
            .NotEmpty().WithMessage("'Username' is a required field.")
            .MaximumLength(60).WithMessage("'Username' must not exceed 60 characters.");

        RuleFor(q => q.Request.DepartureId)
            .NotEmpty().WithMessage("'DepartureId' is a required field.")
            .MaximumLength(100).WithMessage("'DepartureId' must not exceed 100 characters.");

        RuleFor(q => q)
            .Must(ValidateMutuallyExclusiveArrival)
            .WithMessage("'ArrivalId' and 'ArrivalAreaId' are mutually exclusive.");

        RuleFor(q => q)
            .Must(ValidateMutuallyExclusiveTravelModeInterest)
            .WithMessage("'TravelMode' and 'Interest' are mutually exclusive.");

        RuleFor(q => q.Request.ArrivalAreaId)
            .Must(BeValidKgmid)
            .When(q => !string.IsNullOrWhiteSpace(q.Request.ArrivalAreaId))
            .WithMessage("'ArrivalAreaId' must be a valid kgmid starting with /m/ or /g/.");

        RuleFor(q => q)
            .Must(ValidatePassengerCounts)
            .WithMessage("Passenger counts cannot be negative.");

        RuleFor(q => q)
            .Must(ValidateTotalPassengers)
            .WithMessage("Maximum 9 passengers allowed.");

        RuleFor(q => q)
            .Must(ValidateBagsAllowance)
            .When(q => q.Request.Bags.HasValue)
            .WithMessage("'Bags' cannot exceed the number of passengers with carry-on bag allowance.");

        RuleFor(q => q.Request.Month)
            .InclusiveBetween(0, 12)
            .When(q => q.Request.Month.HasValue)
            .WithMessage("'Month' must be between 0 and 12.");

        RuleFor(q => q.Request.TravelDuration)
            .InclusiveBetween(1, 3)
            .When(q => q.Request.TravelDuration.HasValue)
            .WithMessage("'TravelDuration' must be 1 (Weekend), 2 (1 week), or 3 (2 weeks).");

        RuleFor(q => q.Request.TravelClass)
            .InclusiveBetween(1, 4)
            .When(q => q.Request.TravelClass.HasValue)
            .WithMessage("'TravelClass' must be 1 (Economy), 2 (Premium economy), 3 (Business), or 4 (First).");

        RuleFor(q => q.Request.Type)
            .InclusiveBetween(1, 2)
            .When(q => q.Request.Type.HasValue)
            .WithMessage("'Type' must be 1 (Round trip) or 2 (One way).");

        RuleFor(q => q.Request.Stops)
            .InclusiveBetween(0, 3)
            .When(q => q.Request.Stops.HasValue)
            .WithMessage("'Stops' must be 0 (Any), 1 (Nonstop only), 2 (1 stop or fewer), or 3 (2 stops or fewer).");
    }

    private static bool ValidateMutuallyExclusiveArrival(GetFlightExploreQuery query)
    {
        var hasArrivalId = !string.IsNullOrWhiteSpace(query.Request.ArrivalId);
        var hasArrivalAreaId = !string.IsNullOrWhiteSpace(query.Request.ArrivalAreaId);
        return !(hasArrivalId && hasArrivalAreaId);
    }

    private static bool ValidateMutuallyExclusiveTravelModeInterest(GetFlightExploreQuery query)
    {
        // Check if TravelMode has a meaningful value (not null, not 0)
        var hasTravelMode = query.Request.TravelMode.HasValue && query.Request.TravelMode.Value != 0;
        // Check if Interest has a meaningful value (not null/empty)
        // Note: Interest is sent as-is to SerpApi, so we only check for null/whitespace
        var hasInterest = !string.IsNullOrWhiteSpace(query.Request.Interest);
        return !(hasTravelMode && hasInterest);
    }

    private static bool BeValidKgmid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        return value.StartsWith("/m/", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("/g/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidatePassengerCounts(GetFlightExploreQuery query)
    {
        var request = query.Request;
        return (request.Adults ?? 1) >= 0 &&
               (request.Children ?? 0) >= 0 &&
               (request.InfantsInSeat ?? 0) >= 0 &&
               (request.InfantsOnLap ?? 0) >= 0;
    }

    private static bool ValidateTotalPassengers(GetFlightExploreQuery query)
    {
        var request = query.Request;
        var total = (request.Adults ?? 1) + (request.Children ?? 0) + (request.InfantsInSeat ?? 0) + (request.InfantsOnLap ?? 0);
        return total <= 9;
    }

    private static bool ValidateBagsAllowance(GetFlightExploreQuery query)
    {
        var request = query.Request;
        var passengersWithBagAllowance = (request.Adults ?? 1) + (request.Children ?? 0) + (request.InfantsInSeat ?? 0);
        return request.Bags <= passengersWithBagAllowance;
    }
}
