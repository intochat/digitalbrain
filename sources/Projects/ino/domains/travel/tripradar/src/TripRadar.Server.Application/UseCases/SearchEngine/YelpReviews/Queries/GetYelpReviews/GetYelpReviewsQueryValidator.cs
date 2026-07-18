using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpReviews.Queries.GetYelpReviews;

public class GetYelpReviewsQueryValidator : AbstractValidator<GetYelpReviewsQuery>
{
    public GetYelpReviewsQueryValidator()
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

        RuleFor(x => x.Request.Language)
            .MaximumLength(10)
            .WithMessage("Language must not exceed 10 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Language));

        RuleFor(x => x.Request.SortBy)
            .MaximumLength(50)
            .WithMessage("Sortby must not exceed 50 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.SortBy));

        RuleFor(x => x.Request.Rating)
            .InclusiveBetween(1, 5)
            .WithMessage("Rating must be between 1 and 5.")
            .When(x => x.Request.Rating.HasValue);

        RuleFor(x => x.Request.Start)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Start must be zero or a positive number.")
            .When(x => x.Request.Start.HasValue);

        RuleFor(x => x.Request.Num)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Num must be greater than zero.")
            .When(x => x.Request.Num.HasValue);

        RuleFor(x => x.Request.Q)
            .MaximumLength(200)
            .WithMessage("Query must not exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Q));
    }
}
