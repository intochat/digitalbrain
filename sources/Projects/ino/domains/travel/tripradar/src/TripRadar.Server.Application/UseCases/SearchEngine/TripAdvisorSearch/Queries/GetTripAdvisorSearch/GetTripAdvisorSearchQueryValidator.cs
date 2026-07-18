using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.TripAdvisorSearch.Queries.GetTripAdvisorSearch;

public class GetTripAdvisorSearchQueryValidator : AbstractValidator<GetTripAdvisorSearchQuery>
{
    public GetTripAdvisorSearchQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Username is required and must not exceed 50 characters.");

        RuleFor(x => x.Request.Q)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("Query is required and must not exceed 200 characters.");

        RuleFor(x => x.Request.Ssrc)
            .Must(IsValidSsrc)
            .WithMessage("Ssrc must be one of: a, r, A, h, g, v, f.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Ssrc));

        When(x => x.Request.Lat.HasValue || x.Request.Lon.HasValue, () =>
        {
            RuleFor(x => x.Request.Lat)
                .NotNull()
                .WithMessage("Latitude must be provided when longitude is specified.");

            RuleFor(x => x.Request.Lon)
                .NotNull()
                .WithMessage("Longitude must be provided when latitude is specified.");
        });
    }

    private static bool IsValidSsrc(string? ssrc)
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
