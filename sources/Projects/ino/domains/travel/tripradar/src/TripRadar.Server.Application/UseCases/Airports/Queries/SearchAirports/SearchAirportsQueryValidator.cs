using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Airports.Queries.SearchAirports;

public sealed class SearchAirportsQueryValidator : AbstractValidator<SearchAirportsQuery>
{
    public SearchAirportsQueryValidator()
    {
        RuleFor(query => query.Query)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, 20);
    }
}
