using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.LocalPlaces.Queries.GetLocalPlaces;

public class GetLocalPlacesQueryValidator : AbstractValidator<GetLocalPlacesQuery>
{
    public GetLocalPlacesQueryValidator()
    {
        RuleFor(command => command.Request.SearchQuery.Q)
            .NotEmpty()
            .WithMessage("'Query' is a required field.")
            .MaximumLength(200)
            .WithMessage("'Query' must not exceed 200 characters.");

        When(command => command.Request.GeographicLocationDto is not null, () =>
        {
            RuleFor(command => command.Request.GeographicLocationDto!.Location)
                .MaximumLength(200)
                .WithMessage("'Location' must not exceed 200 characters.")
                .When(command => !string.IsNullOrEmpty(command.Request.GeographicLocationDto!.Location));

            RuleFor(command => command.Request.GeographicLocationDto!.Uule)
                .MaximumLength(500)
                .WithMessage("'Uule' must not exceed 500 characters.")
                .When(command => !string.IsNullOrEmpty(command.Request.GeographicLocationDto!.Uule));

            RuleFor(command => command.Request.GeographicLocationDto!)
                .Must(parameters =>
                    string.IsNullOrEmpty(parameters.Location) ||
                    string.IsNullOrEmpty(parameters.Uule))
                .WithMessage("'Location' and 'Uule' cannot be used together.");
        });

        When(command => command.Request.Filters is not null, () =>
        {
            RuleFor(command => command.Request.Filters!.Tbs)
                .MaximumLength(1000)
                .WithMessage("'Tbs' must not exceed 1000 characters.")
                .When(command => !string.IsNullOrEmpty(command.Request.Filters!.Tbs));
        });

        When(command => command.Request.Pagination is not null, () =>
        {
            RuleFor(command => command.Request.Pagination!.Start)
                .GreaterThanOrEqualTo(0)
                .WithMessage("'Start' must be greater than or equal to 0.")
                .When(command => command.Request.Pagination!.Start.HasValue);
        });

        RuleFor(command => command.Username)
            .NotEmpty()
            .WithMessage("'Username' is a required field.")
            .MaximumLength(60)
            .WithMessage("'Username' must not exceed 60 characters.");
    }
}
