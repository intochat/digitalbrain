using FluentValidation;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.UseCases.Common.Validators;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionQuery;

public class UpdateScheduledExecutionQueryCommandValidator : BaseScheduledQueryValidator<UpdateScheduledExecutionQueryCommand>
{
    public UpdateScheduledExecutionQueryCommandValidator(IReferenceLookupValidator referenceLookupValidator)
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters.");

        RuleFor(x => x.ScheduledExecutionUniqueId)
            .NotEmpty().WithMessage("Scheduled execution unique ID is required.");

        RuleFor(x => x.SearchQuery)
            .Must(searchQuery => searchQuery is null || !string.IsNullOrWhiteSpace(searchQuery))
            .WithMessage("Search query cannot be empty.");

        RuleFor(x => x.Location)
            .Must(location => location is null || !string.IsNullOrWhiteSpace(location))
            .WithMessage("Location cannot be empty.");

        RuleFor(x => x.Radius)
            .Must(radius => !radius.HasValue || radius.Value > 0)
            .WithMessage("Radius must be greater than zero.");

        RuleFor(x => x.DepartureAirportCode)
            .Must(code => code is null || (code.Length == 3 && code.All(char.IsLetter)))
            .WithMessage("Departure airport code must be 3 letters.");

        RuleFor(x => x.DestinationAirportCode)
            .Must(code => code is null || (code.Length == 3 && code.All(char.IsLetter)))
            .WithMessage("Destination airport code must be 3 letters.");

        RuleFor(x => x)
            .Must(x => x.DepartureAirportCode is null || x.DestinationAirportCode is null ||
                       !string.Equals(x.DepartureAirportCode, x.DestinationAirportCode, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Departure and destination airport codes must be different.");

        RuleFor(x => x)
            .Must(x => !x.DepartureDate.HasValue || !x.ReturnDate.HasValue || x.ReturnDate.Value > x.DepartureDate.Value)
            .WithMessage("Return date must be after departure date.");

        RuleFor(x => x)
            .Must(x => !x.CheckInDate.HasValue || !x.CheckOutDate.HasValue || x.CheckOutDate.Value > x.CheckInDate.Value)
            .WithMessage("Check-out date must be after check-in date.");

        RuleFor(x => x.AdditionalParametersJson)
            .CustomAsync(async (json, context, cancellationToken) =>
            {
                var includeAirlines = ReadJsonStringParameter(json, "include_airlines", "includeAirlines");
                var excludeAirlines = ReadJsonStringParameter(json, "exclude_airlines", "excludeAirlines");

                if (!string.IsNullOrWhiteSpace(includeAirlines) && !string.IsNullOrWhiteSpace(excludeAirlines))
                {
                    context.AddFailure(nameof(UpdateScheduledExecutionQueryCommand.AdditionalParametersJson), "include_airlines and exclude_airlines cannot both be set.");
                    return;
                }

                var includeAirlinesError = await referenceLookupValidator.ValidateAirlineCodesAsync(includeAirlines, cancellationToken);
                if (includeAirlinesError is not null)
                {
                    context.AddFailure(nameof(UpdateScheduledExecutionQueryCommand.AdditionalParametersJson), includeAirlinesError.Reason);
                }

                var excludeAirlinesError = await referenceLookupValidator.ValidateAirlineCodesAsync(excludeAirlines, cancellationToken);
                if (excludeAirlinesError is not null)
                {
                    context.AddFailure(nameof(UpdateScheduledExecutionQueryCommand.AdditionalParametersJson), excludeAirlinesError.Reason);
                }
            });
    }
}
