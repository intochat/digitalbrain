using FluentValidation;
using TripRadar.Server.Application.UseCases.Common.Providers;
using TripRadar.Server.Application.UseCases.Common.Validators;

namespace TripRadar.Server.Application.UseCases.SearchEngine.LocalPlaces.Commands.CreateScheduledLocalPlacesQuery;

public class
    CreateScheduledLocalPlacesQueryCommandValidator : BaseScheduledQueryValidator<
    CreateScheduledLocalPlacesQueryCommand>
{
    public CreateScheduledLocalPlacesQueryCommandValidator()
    {
        var columnHierarchyProvider = new LocalPlacesColumnHierarchyProvider();

        RuleFor(x => x.SearchQuery)
            .NotEmpty()
            .WithMessage("Query is required")
            .MaximumLength(200)
            .WithMessage("Query must not exceed 200 characters");

        RuleFor(x => x.Location)
            .NotEmpty()
            .WithMessage("Location is required")
            .MaximumLength(200)
            .WithMessage("Location must not exceed 200 characters");

        RuleFor(x => x.Radius)
            .InclusiveBetween(1, 50000)
            .When(x => x.Radius.HasValue)
            .WithMessage("Radius must be between 1 and 50,000 meters");


        RuleFor(x => x.SelectedColumns)
            .Must(columns => columns != null && columns.All(col => columnHierarchyProvider.IsValidColumn(col.Name)))
            .WithMessage("One or more invalid columns specified")
            .Must(columns =>
                columns != null && columns.All(col => columnHierarchyProvider.GetRootColumn(col.Name) != null))
            .WithMessage("Invalid column hierarchy specified");

        // Additional parameters validation
        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            code => !string.IsNullOrEmpty(code) && code.Length == 2,
            "Country code must be 2 characters",
            "gl");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            code => !string.IsNullOrEmpty(code) && code.Length == 2,
            "Language code must be 2 characters",
            "hl");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            currency => !string.IsNullOrEmpty(currency) && currency.Length == 3,
            "Currency code must be 3 characters",
            "currency");

        // Location parameter validation from AdditionalParametersJson
        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            location => string.IsNullOrEmpty(location) || location.Length <= 200,
            "Location must not exceed 200 characters",
            "location");

        AddJsonParamRule<double>(
            x => x.AdditionalParametersJson,
            latitude => latitude >= -90 && latitude <= 90,
            "Latitude must be between -90 and 90 degrees",
            "latitude");

        AddJsonParamRule<double>(
            x => x.AdditionalParametersJson,
            longitude => longitude >= -180 && longitude <= 180,
            "Longitude must be between -180 and 180 degrees",
            "longitude");

        AddJsonParamRule<int>(
            x => x.AdditionalParametersJson,
            radius => radius > 0 && radius <= 50000,
            "Radius must be between 1 and 50,000 meters",
            "radius");

        AddJsonParamRule<double>(
            x => x.AdditionalParametersJson,
            rating => rating >= 0 && rating <= 5,
            "Minimum rating must be between 0 and 5",
            "minRating");

        AddJsonParamRule<double>(
            x => x.AdditionalParametersJson,
            rating => rating >= 0 && rating <= 5,
            "Maximum rating must be between 0 and 5",
            "maxRating");

        AddJsonParamRule<string[]>(
            x => x.AdditionalParametersJson,
            placeTypes => placeTypes != null && placeTypes.All(pt => !string.IsNullOrEmpty(pt)),
            "Place types must not be empty",
            "placeTypes");

        AddJsonParamRule<string[]>(
            x => x.AdditionalParametersJson,
            serviceOptions => serviceOptions != null && serviceOptions.All(so => !string.IsNullOrEmpty(so)),
            "Service options must not be empty",
            "serviceOptions");

        AddJsonParamRule<int>(
            x => x.AdditionalParametersJson,
            start => start >= 0,
            "Start index must be non-negative",
            "start");

        AddJsonParamRule<int>(
            x => x.AdditionalParametersJson,
            num => num > 0 && num <= 100,
            "Number of results must be between 1 and 100",
            "num");
    }
}
