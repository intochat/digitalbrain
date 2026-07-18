using FluentValidation;
using TripRadar.Server.Application.Constants.Flights;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.Common.Providers;
using TripRadar.Server.Application.UseCases.Common.Validators;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Flights.Commands.CreateScheduledFlightQuery;

public class CreateScheduledFlightQueryCommandValidator : BaseScheduledQueryValidator<CreateScheduledFlightQueryCommand>
{
    public CreateScheduledFlightQueryCommandValidator(
        IReferenceLookupValidator referenceLookupValidator,
        IScheduledExecutionValidityService scheduledExecutionValidityService)
    {
        var columnHierarchyProvider = new FlightColumnHierarchyProvider();

        RuleFor(x => x.DepartureAirportCode)
            .NotEmpty()
            .WithMessage("DepartureAirportCode is required")
            .Length(3)
            .WithMessage("DepartureAirportCode must be exactly 3 characters")
            .Matches("^[A-Z]{3}$")
            .WithMessage("DepartureAirportCode must be 3 uppercase letters");

        RuleFor(x => x.DestinationAirportCode)
            .NotEmpty()
            .WithMessage("DestinationAirportCode is required")
            .Length(3)
            .WithMessage("DestinationAirportCode must be exactly 3 characters")
            .Matches("^[A-Z]{3}$")
            .WithMessage("DestinationAirportCode must be 3 uppercase letters")
            .Must((command, destinationCode) => !string.Equals(destinationCode, command.DepartureAirportCode, StringComparison.OrdinalIgnoreCase))
            .WithMessage("DestinationAirportCode must be different from DepartureAirportCode");

        RuleFor(x => x.DepartureDate)
            .NotEmpty()
            .WithMessage("Departure date is required")
            .Must(date => date.Date >= DateTime.UtcNow.Date)
            .WithMessage("Departure date must not be in the past");

        RuleFor(x => x.ReturnDate)
            .Must((command, returnDate) => !returnDate.HasValue || returnDate.Value > command.DepartureDate)
            .WithMessage("Return date must be after departure date");

        RuleFor(x => x)
            .Must(command => !command.NextExecutionTime.HasValue || scheduledExecutionValidityService.IsExecutableAtNextRun(
                ScheduledExecutionSearchType.Flights,
                command.NextExecutionTime.Value,
                command.DepartureDate))
            .WithMessage("Next execution time must be on or before departure date.");

        RuleFor(x => x.SelectedColumns)
            .Must(columns => columns != null && columns.All(col => columnHierarchyProvider.IsValidColumn(col.Name)))
            .WithMessage("One or more invalid selected columns specified")
            .Must(columns =>
                columns != null && columns.All(col => columnHierarchyProvider.GetRootColumn(col.Name) != null))
            .WithMessage("Invalid column hierarchy specified");

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
            code => !string.IsNullOrEmpty(code) && code.Length == 3,
            "Currency code must be 3 characters",
            "currency");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            cabin => FlightQueryConstants.CabinClasses.Contains(cabin),
            "Invalid cabin class",
            "cabin_class");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            travelClass => FlightQueryConstants.TravelClasses.Contains(travelClass),
            "Invalid travel class",
            "travel_class");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            sort => FlightQueryConstants.SortBy.Contains(sort),
            "Invalid sort by option",
            "sort_by");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            stops => FlightQueryConstants.Stops.Contains(stops),
            "Invalid number of stops",
            "stops");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            type => FlightQueryConstants.FlightTypes.Contains(type),
            "Invalid flight type",
            "flight_type");

        AddJsonParamRule<string>(
            x => x.AdditionalParametersJson,
            emissions => FlightQueryConstants.EmissionsTypes.Contains(emissions),
            "Invalid emissions type",
            "emissions_type");

        AddJsonParamRule<int>(
            x => x.AdditionalParametersJson,
            count => count is >= 1 and <= 9,
            "Adults must be between 1 and 9",
            "adults");

        AddJsonParamRule<int>(
            x => x.AdditionalParametersJson,
            count => count is >= 0 and <= 9,
            "Children must be between 0 and 9",
            "children");

        AddJsonParamRule<int>(
            x => x.AdditionalParametersJson,
            count => count is >= 0 and <= 9,
            "Infants must be between 0 and 9",
            "infants");

        RuleFor(x => x.AdditionalParametersJson)
            .CustomAsync(async (json, context, cancellationToken) =>
            {
                var includeAirlines = ReadJsonStringParameter(json, "include_airlines", "includeAirlines");
                var excludeAirlines = ReadJsonStringParameter(json, "exclude_airlines", "excludeAirlines");

                if (!string.IsNullOrWhiteSpace(includeAirlines) && !string.IsNullOrWhiteSpace(excludeAirlines))
                {
                    context.AddFailure(nameof(CreateScheduledFlightQueryCommand.AdditionalParametersJson), "include_airlines and exclude_airlines cannot both be set.");
                    return;
                }

                var includeAirlinesError = await referenceLookupValidator.ValidateAirlineCodesAsync(includeAirlines, cancellationToken);
                if (includeAirlinesError is not null)
                {
                    context.AddFailure(nameof(CreateScheduledFlightQueryCommand.AdditionalParametersJson), includeAirlinesError.Reason);
                }

                var excludeAirlinesError = await referenceLookupValidator.ValidateAirlineCodesAsync(excludeAirlines, cancellationToken);
                if (excludeAirlinesError is not null)
                {
                    context.AddFailure(nameof(CreateScheduledFlightQueryCommand.AdditionalParametersJson), excludeAirlinesError.Reason);
                }
            });
    }
}
