using FluentValidation;

namespace TripRadar.Server.Application.UseCases.PlaceReviews.Queries.GetPlaceReviews;

public class GetPlaceReviewsQueryValidator : AbstractValidator<GetPlaceReviewsQuery>
{
    public GetPlaceReviewsQueryValidator()
    {
        RuleFor(command => command.Request)
            .Must(r => !string.IsNullOrEmpty(r.PlaceId) || !string.IsNullOrEmpty(r.DataId))
            .WithMessage("Either 'PlaceId' or 'DataId' must be provided.");

        RuleFor(command => command.Request.PlaceId)
            .MaximumLength(200)
            .WithMessage("'PlaceId' must not exceed 200 characters.")
            .When(command => !string.IsNullOrEmpty(command.Request.PlaceId));

        RuleFor(command => command.Request.DataId)
            .MaximumLength(200)
            .WithMessage("'DataId' must not exceed 200 characters.")
            .When(command => !string.IsNullOrEmpty(command.Request.DataId));

        When(command => command.Request.Filters is not null, () =>
        {
            RuleFor(command => command.Request.Filters!.SortBy)
                .Must(sortBy => string.IsNullOrWhiteSpace(sortBy) || IsValidSortBy(sortBy))
                .WithMessage("'SortBy' must be one of: qualityScore, newestFirst, ratingHigh, ratingLow.")
                .When(command => command.Request.Filters!.SortBy != null);

            RuleFor(command => command.Request.Filters!.TopicId)
                .MaximumLength(100)
                .WithMessage("'TopicId' must not exceed 100 characters.")
                .When(command => command.Request.Filters!.TopicId != null);
        });

        When(command => command.Request.Pagination is not null, () =>
        {
            RuleFor(command => command.Request.Pagination!.Num)
                .InclusiveBetween(1, 40)
                .WithMessage("'Num' must be between 1 and 40.")
                .When(command => command.Request.Pagination!.Num.HasValue);

            RuleFor(command => command.Request.Pagination!.NextPageToken)
                .MaximumLength(500)
                .WithMessage("'NextPageToken' must not exceed 500 characters.")
                .When(command => command.Request.Pagination!.NextPageToken != null);
        });

        RuleFor(command => command.Username)
            .NotEmpty()
            .WithMessage("'Username' is required.")
            .MaximumLength(100)
            .WithMessage("'Username' must not exceed 100 characters.");
    }

    private static bool IsValidSortBy(string sortBy)
    {
        var validSortByValues = new[] { "qualityScore", "newestFirst", "ratingHigh", "ratingLow" };
        return validSortByValues.Contains(sortBy, StringComparer.OrdinalIgnoreCase);
    }
}
