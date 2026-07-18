using FluentValidation;
using TripRadar.Server.Application.DTO.Requests;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserPreferences;

public sealed class UpdateUserPreferencesCommandValidator : AbstractValidator<UpdateUserPreferencesCommand>
{
    public UpdateUserPreferencesCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters.");

        RuleFor(x => x.Preferences)
            .NotNull().WithMessage("Preferences cannot be null.");

        // Validate Flight preferences
        When(x => x.Preferences?.Flight != null, () => {
            RuleFor(x => x.Preferences!.Flight!.Adults)
                .InclusiveBetween(1, 10).WithMessage("Adults must be between 1 and 10.")
                .When(x => x.Preferences!.Flight!.Adults.HasValue);

            RuleFor(x => x.Preferences!.Flight!.Children)
                .InclusiveBetween(0, 10).WithMessage("Children must be between 0 and 10.")
                .When(x => x.Preferences!.Flight!.Children.HasValue);

            RuleFor(x => x.Preferences!.Flight!.InfantsInSeat)
                .InclusiveBetween(0, 10).WithMessage("InfantsInSeat must be between 0 and 10.")
                .When(x => x.Preferences!.Flight!.InfantsInSeat.HasValue);

            RuleFor(x => x.Preferences!.Flight!.InfantsOnLap)
                .InclusiveBetween(0, 10).WithMessage("InfantsOnLap must be between 0 and 10.")
                .When(x => x.Preferences!.Flight!.InfantsOnLap.HasValue);

            RuleFor(x => x.Preferences!.Flight!.MaxPrice)
                .GreaterThan(0).WithMessage("MaxPrice must be greater than 0.")
                .When(x => x.Preferences!.Flight!.MaxPrice.HasValue);

            RuleFor(x => x.Preferences!.Flight!.Currency)
                .Length(3).WithMessage("Currency must be a 3-character code.")
                .When(x => !string.IsNullOrEmpty(x.Preferences!.Flight!.Currency));
        });

        // Validate Hotel preferences
        When(x => x.Preferences?.Hotel != null, () => {
            RuleFor(x => x.Preferences!.Hotel!.Adults)
                .InclusiveBetween(1, 20).WithMessage("Adults must be between 1 and 20.")
                .When(x => x.Preferences!.Hotel!.Adults.HasValue);

            RuleFor(x => x.Preferences!.Hotel!.Children)
                .InclusiveBetween(0, 10).WithMessage("Children must be between 0 and 10.")
                .When(x => x.Preferences!.Hotel!.Children.HasValue);

            RuleFor(x => x.Preferences!.Hotel!.MinPrice)
                .GreaterThan(0).WithMessage("MinPrice must be greater than 0.")
                .When(x => x.Preferences!.Hotel!.MinPrice.HasValue);

            RuleFor(x => x.Preferences!.Hotel!.MaxPrice)
                .GreaterThan(0).WithMessage("MaxPrice must be greater than 0.")
                .When(x => x.Preferences!.Hotel!.MaxPrice.HasValue);

            RuleFor(x => x.Preferences!.Hotel!)
                .Must(x => !x.MinPrice.HasValue || !x.MaxPrice.HasValue || x.MinPrice <= x.MaxPrice)
                .WithMessage("MinPrice cannot be greater than MaxPrice.");

            RuleFor(x => x.Preferences!.Hotel!.Currency)
                .Length(3).WithMessage("Currency must be a 3-character code.")
                .When(x => !string.IsNullOrEmpty(x.Preferences!.Hotel!.Currency));

            RuleFor(x => x.Preferences!.Hotel!.Rating)
                .Must(rating => string.IsNullOrEmpty(rating) || IsValidHotelRating(rating))
                .WithMessage("Hotel rating must be between 1 and 5.")
                .When(x => !string.IsNullOrEmpty(x.Preferences!.Hotel!.Rating));
        });

        // Validate TripAdvisor search preferences
        When(x => x.Preferences?.TripAdvisorSearch != null, () => {
            RuleFor(x => x.Preferences!.TripAdvisorSearch!.Ssrc)
                .Must(IsValidTripAdvisorSsrc)
                .WithMessage("Ssrc must be one of: a, r, A, h, g, v, f.")
                .When(x => !string.IsNullOrWhiteSpace(x.Preferences!.TripAdvisorSearch!.Ssrc));
        });

        // Ensure at least one preference type is provided
        RuleFor(x => x.Preferences)
            .Must(HaveAtLeastOnePreference)
            .WithMessage("At least one preference type must be provided.")
            .When(x => x.Preferences != null);
    }

    private static bool IsValidHotelRating(string rating)
    {
        if (int.TryParse(rating, out var ratingValue))
        {
            return ratingValue >= 1 && ratingValue <= 5;
        }
        return false;
    }

    private static bool HaveAtLeastOnePreference(UserPreferencePatchRequestDTO? preferences)
    {
        if (preferences == null)
        {
            return false;
        }

        return preferences.Flight != null ||
               preferences.Hotel != null ||
               preferences.Event != null ||
               preferences.LocalPlaces != null ||
               preferences.Maps != null ||
               preferences.PlaceReview != null ||
               preferences.TripAdvisorSearch != null;
    }

    private static bool IsValidTripAdvisorSsrc(string? ssrc)
    {
        if (string.IsNullOrWhiteSpace(ssrc))
        {
            return true;
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", // All Results
            "r", // Restaurants
            "A", // Things to Do
            "h", // Hotels
            "g", // Destinations
            "v", // Vacation Rentals
            "f"  // Forums
        };

        return allowed.Contains(ssrc.Trim());
    }
}
