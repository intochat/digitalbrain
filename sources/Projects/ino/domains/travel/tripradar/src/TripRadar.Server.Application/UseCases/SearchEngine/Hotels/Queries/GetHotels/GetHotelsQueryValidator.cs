using System.Globalization;
using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Queries.GetHotels;

public class GetHotelsQueryValidator : AbstractValidator<GetHotelsQuery>
{
    public GetHotelsQueryValidator()
    {
        RuleFor(command => command.GetHotelRequestDto.SearchQuery.Q)
            .NotEmpty()
            .WithMessage("'Search query' is a required field.");

        RuleFor(command => command.GetHotelRequestDto.AdvancedParameters.CheckInDate)
            .NotEmpty()
            .WithMessage("'CheckInDate' is a required field.")
            .Must(date => TryParseIsoDate(date, out _))
            .WithMessage("'CheckInDate' must be in yyyy-MM-dd format.");

        RuleFor(command => command.GetHotelRequestDto.AdvancedParameters.CheckOutDate)
            .NotEmpty()
            .WithMessage("'CheckOutDate' is a required field.")
            .Must(date => TryParseIsoDate(date, out _))
            .WithMessage("'CheckOutDate' must be in yyyy-MM-dd format.");

        RuleFor(command => command)
            .Must(HasValidDateRange)
            .WithMessage("Check-out date must be greater than or equal to check-in date.")
            .When(command =>
                !string.IsNullOrWhiteSpace(command.GetHotelRequestDto.AdvancedParameters.CheckInDate) &&
                !string.IsNullOrWhiteSpace(command.GetHotelRequestDto.AdvancedParameters.CheckOutDate));

        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("'Username' is a required field.")
            .MaximumLength(60).WithMessage("'Username' must not exceed 60 characters.");
    }

    private static bool HasValidDateRange(GetHotelsQuery command)
    {
        var advanced = command.GetHotelRequestDto.AdvancedParameters;
        if (!TryParseIsoDate(advanced.CheckInDate, out var checkInDate))
        {
            return false;
        }

        if (!TryParseIsoDate(advanced.CheckOutDate, out var checkOutDate))
        {
            return false;
        }

        return checkOutDate >= checkInDate;
    }

    private static bool TryParseIsoDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
