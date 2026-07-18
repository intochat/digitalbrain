using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpSearch.Queries.GetYelpSearch;

public class GetYelpSearchQueryValidator : AbstractValidator<GetYelpSearchQuery>
{
    public GetYelpSearchQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Username is required and must not exceed 50 characters.");

        RuleFor(x => x.Request.FindLoc)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("Location is required and must not exceed 200 characters.");

        RuleFor(x => x.Request.FindDesc)
            .MaximumLength(200)
            .WithMessage("Description must not exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.FindDesc));

        RuleFor(x => x.Request.YelpDomain)
            .MaximumLength(100)
            .WithMessage("Yelp domain must not exceed 100 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.YelpDomain));

        RuleFor(x => x.Request.SortBy)
            .MaximumLength(50)
            .WithMessage("Sortby must not exceed 50 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.SortBy));

        RuleFor(x => x.Request.Attrs)
            .MaximumLength(200)
            .WithMessage("Attrs must not exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Attrs));

        RuleFor(x => x.Request.Cflt)
            .MaximumLength(200)
            .WithMessage("Cflt must not exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Cflt));

        RuleFor(x => x.Request.Start)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Start must be zero or a positive number.")
            .When(x => x.Request.Start.HasValue);
    }
}
