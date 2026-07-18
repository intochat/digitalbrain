using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.TripAdvisorPlace.Queries.GetTripAdvisorPlace;

public class GetTripAdvisorPlaceQueryValidator : AbstractValidator<GetTripAdvisorPlaceQuery>
{
    public GetTripAdvisorPlaceQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Username is required and must not exceed 50 characters.");

        RuleFor(x => x.Request.PlaceId)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("PlaceId is required and must not exceed 200 characters.");
    }
}
