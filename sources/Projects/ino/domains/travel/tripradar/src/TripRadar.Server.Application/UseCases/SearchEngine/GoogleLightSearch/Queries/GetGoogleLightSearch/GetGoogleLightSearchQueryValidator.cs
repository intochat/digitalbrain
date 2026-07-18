using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.GoogleLightSearch.Queries.GetGoogleLightSearch;

public class GetGoogleLightSearchQueryValidator : AbstractValidator<GetGoogleLightSearchQuery>
{
    private static readonly string[] AllowedDevices = ["desktop", "tablet", "mobile"];
    private static readonly string[] AllowedSafeValues = ["active", "off"];
    private static readonly string[] AllowedOutputValues = ["json", "html"];

    public GetGoogleLightSearchQueryValidator()
    {
        RuleFor(command => command.Request.SearchQuery)
            .NotNull()
            .WithMessage("'SearchQuery' is a required field.");

        RuleFor(command => command.Request.SearchQuery!.Q)
            .NotEmpty()
            .When(command => command.Request.SearchQuery != null)
            .WithMessage("'Q' is a required field.");

        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("'Username' is a required field.")
            .MaximumLength(60).WithMessage("'Username' must not exceed 60 characters.");

        RuleFor(command => command.Request)
            .Must(request => request.GeographicLocation == null ||
                             string.IsNullOrWhiteSpace(request.GeographicLocation.Location) ||
                             string.IsNullOrWhiteSpace(request.GeographicLocation.Uule))
            .WithMessage("location and uule cannot both be set.");

        RuleFor(command => command.Request)
            .Must(request => request.NoCache != true || request.Async != true)
            .WithMessage("no_cache and async cannot both be true.");

        RuleFor(command => command.Request.Pagination!.Start)
            .GreaterThanOrEqualTo(0)
            .When(command => command.Request.Pagination?.Start.HasValue == true)
            .WithMessage("'Start' must be zero or a positive number.");

        RuleFor(command => command.Request.Safe)
            .Must(value => string.IsNullOrWhiteSpace(value) || AllowedSafeValues.Contains(value))
            .WithMessage("Safe must be either 'active' or 'off'.");

        RuleFor(command => command.Request.Device)
            .Must(value => string.IsNullOrWhiteSpace(value) || AllowedDevices.Contains(value))
            .WithMessage("Device must be 'desktop', 'tablet', or 'mobile'.");

        RuleFor(command => command.Request.Output)
            .Must(value => string.IsNullOrWhiteSpace(value) || AllowedOutputValues.Contains(value))
            .WithMessage("Output must be either 'json' or 'html'.");
    }
}
