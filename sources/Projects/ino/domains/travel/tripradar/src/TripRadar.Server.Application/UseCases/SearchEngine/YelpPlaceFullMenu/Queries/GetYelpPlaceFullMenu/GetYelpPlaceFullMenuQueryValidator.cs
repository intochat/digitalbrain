using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpPlaceFullMenu.Queries.GetYelpPlaceFullMenu;

public class GetYelpPlaceFullMenuQueryValidator : AbstractValidator<GetYelpPlaceFullMenuQuery>
{
    public GetYelpPlaceFullMenuQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Username is required and must not exceed 50 characters.");

        RuleFor(x => x.Request.PlaceId)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("PlaceId is required and must not exceed 200 characters.");

        RuleFor(x => x.Request.YelpDomain)
            .MaximumLength(100)
            .WithMessage("Yelp domain must not exceed 100 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.YelpDomain));

        RuleFor(x => x.Request.MenuName)
            .MaximumLength(200)
            .WithMessage("Menu name must not exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.MenuName));
    }
}
