using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlightPriceCalendar;

public class GetFlightPriceCalendarQueryValidator : AbstractValidator<GetFlightPriceCalendarQuery>
{
    public GetFlightPriceCalendarQueryValidator()
    {
        RuleFor(q => q.Username)
            .NotEmpty().WithMessage("'Username' is a required field.")
            .MaximumLength(60).WithMessage("'Username' must not exceed 60 characters.");

        RuleFor(q => q.Request.DepartureId)
            .NotEmpty().WithMessage("'DepartureId' is a required field.")
            .MaximumLength(100).WithMessage("'DepartureId' must not exceed 100 characters.");

        RuleFor(q => q.Request.ArrivalId)
            .NotEmpty().WithMessage("'ArrivalId' is a required field.")
            .MaximumLength(100).WithMessage("'ArrivalId' must not exceed 100 characters.");

        RuleFor(q => q.Request.Month)
            .InclusiveBetween(1, 12)
            .WithMessage("'Month' must be between 1 and 12.");

        RuleFor(q => q.Request.Year)
            .InclusiveBetween(2024, 2030)
            .WithMessage("'Year' must be between 2024 and 2030.");

        RuleFor(q => q.Request.TripLengthDays)
            .InclusiveBetween(1, 30)
            .When(q => q.Request.TripLengthDays.HasValue)
            .WithMessage("'TripLengthDays' must be between 1 and 30.");
    }
}
