using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.OpenTableReviews.Queries.GetOpenTableReviews;

public class GetOpenTableReviewsQueryValidator : AbstractValidator<GetOpenTableReviewsQuery>
{
    public GetOpenTableReviewsQueryValidator()
    {
        RuleFor(x => x.Request.Rid)
            .NotEmpty()
            .WithMessage("Rid is required.")
            .MaximumLength(200)
            .WithMessage("Rid must not exceed 200 characters.");

        RuleFor(x => x.Request.OpenTableDomain)
            .MaximumLength(100)
            .WithMessage("OpenTable domain must not exceed 100 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.OpenTableDomain));

        RuleFor(x => x.Request.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.")
            .When(x => x.Request.Page.HasValue);
    }
}
