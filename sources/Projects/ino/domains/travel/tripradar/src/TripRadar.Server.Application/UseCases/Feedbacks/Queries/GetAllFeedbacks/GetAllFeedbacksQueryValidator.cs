using FluentValidation;
using TripRadar.Server.Application.Constants;

namespace TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetAllFeedbacks;

public sealed class GetAllFeedbacksQueryValidator : AbstractValidator<GetAllFeedbacksQuery>
{
    public GetAllFeedbacksQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(ValidationConstants.MinLimit, ValidationConstants.MaxLimit);
    }
}
