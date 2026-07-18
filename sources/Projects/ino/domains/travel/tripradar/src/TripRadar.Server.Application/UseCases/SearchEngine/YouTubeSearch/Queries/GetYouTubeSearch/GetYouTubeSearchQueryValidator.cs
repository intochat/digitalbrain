using FluentValidation;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YouTubeSearch.Queries.GetYouTubeSearch;

public class GetYouTubeSearchQueryValidator : AbstractValidator<GetYouTubeSearchQuery>
{
    public GetYouTubeSearchQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(50)
            .WithMessage("Username is required and must not exceed 50 characters.");

        RuleFor(x => x.Request.SearchQuery)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("SearchQuery is required and must not exceed 200 characters.");

        RuleFor(x => x.Request.Gl)
            .Length(2)
            .WithMessage("Gl must be exactly 2 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Gl));

        RuleFor(x => x.Request.Hl)
            .Length(2)
            .WithMessage("Hl must be exactly 2 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Hl));

        RuleFor(x => x.Request.Output)
            .Must(output => string.Equals(output, "json", StringComparison.Ordinal) ||
                            string.Equals(output, "html", StringComparison.Ordinal))
            .WithMessage("Output must be either 'json' or 'html'.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Output));

        RuleFor(x => x.Request)
            .Must(request => request is not { NoCache: true, Async: true })
            .WithMessage("no_cache and async cannot both be true.");
    }
}
