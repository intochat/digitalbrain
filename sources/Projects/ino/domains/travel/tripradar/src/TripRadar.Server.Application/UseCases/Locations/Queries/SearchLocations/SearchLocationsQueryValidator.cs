using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Locations.Queries.SearchLocations;

public sealed class SearchLocationsQueryValidator : AbstractValidator<SearchLocationsQuery>
{
    public SearchLocationsQueryValidator()
    {
        RuleFor(query => query.Query)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, 20);
    }
}
